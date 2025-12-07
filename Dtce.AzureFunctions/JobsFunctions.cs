using System.Net;
using System.Text.Json;
using Dtce.Common;
using Dtce.Identity;
using Dtce.JobQueue;
using Dtce.Persistence;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Dtce.AzureFunctions;

public class JobsFunctions
{
    private readonly IMessageProducer _messageProducer;
    private readonly IJobStatusRepository _jobStatusRepository;
    private readonly IObjectStorage _objectStorage;
    private readonly ILogger<JobsFunctions> _logger;
    private readonly IUserService _userService;

    public JobsFunctions(
        IMessageProducer messageProducer,
        IJobStatusRepository jobStatusRepository,
        IObjectStorage objectStorage,
        ILogger<JobsFunctions> logger,
        IUserService userService)
    {
        _messageProducer = messageProducer;
        _jobStatusRepository = jobStatusRepository;
        _objectStorage = objectStorage;
        _logger = logger;
        _userService = userService;
    }

    [Function("SubmitJob")]
    public async Task<HttpResponseData> SubmitJob(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/v1/jobs/submit")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger("SubmitJob");
        var response = req.CreateResponse(HttpStatusCode.Accepted);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            // Check for API key authentication
            if (!await ValidateApiKeyAsync(req, logger))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await errorResponse.WriteStringAsync("Invalid or missing API key");
                return errorResponse;
            }

            // Parse request - Azure Functions isolated model doesn't support ReadFormDataAsync
            // We'll need to parse multipart form data manually or use a different approach
            // For now, accept JSON body with base64 encoded file or URL
            string? documentUrl = null;
            byte[]? documentData = null;
            string? fileName = null;
            string? contentType = null;

            if (req.Body != null)
            {
                using var reader = new StreamReader(req.Body);
                var body = await reader.ReadToEndAsync();
                
                if (!string.IsNullOrEmpty(body))
                {
                    try
                    {
                        var requestJson = JsonSerializer.Deserialize<JsonElement>(body);
                        if (requestJson.TryGetProperty("documentUrl", out var urlElement))
                        {
                            documentUrl = urlElement.GetString();
                        }
                        if (requestJson.TryGetProperty("documentData", out var dataElement))
                        {
                            var base64Data = dataElement.GetString();
                            if (!string.IsNullOrEmpty(base64Data))
                            {
                                documentData = Convert.FromBase64String(base64Data);
                            }
                        }
                        if (requestJson.TryGetProperty("fileName", out var nameElement))
                        {
                            fileName = nameElement.GetString();
                        }
                        if (requestJson.TryGetProperty("contentType", out var typeElement))
                        {
                            contentType = typeElement.GetString();
                        }
                    }
                    catch
                    {
                        // If JSON parsing fails, try to read as multipart (simplified)
                    }
                }
            }

            // Validation
            if (documentData == null && string.IsNullOrWhiteSpace(documentUrl))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteStringAsync(JsonSerializer.Serialize(new { error = "Either document file or document_url must be provided" }));
                return badRequest;
            }

            if (documentData != null && !string.IsNullOrEmpty(fileName))
            {
                var allowedExtensions = new[] { ".docx", ".pdf" };
                var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
                if (!Array.Exists(allowedExtensions, ext => ext == fileExtension))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteStringAsync(JsonSerializer.Serialize(new { error = "Invalid file type. Only .docx and .pdf are supported" }));
                    return badRequest;
                }

                const long maxFileSize = 50 * 1024 * 1024; // 50MB
                if (documentData.Length > maxFileSize)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteStringAsync(JsonSerializer.Serialize(new { error = "File size exceeds 50MB limit" }));
                    return badRequest;
                }
            }

            // Generate job ID
            var jobId = Guid.NewGuid().ToString();

            // Determine document type
            DocumentType documentType;
            if (documentData != null && !string.IsNullOrEmpty(fileName))
            {
                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                documentType = extension == ".docx" ? DocumentType.Docx : DocumentType.Pdf;
            }
            else
            {
                documentType = DocumentType.GoogleDoc;
            }

            // Create job request
            var jobRequest = new JobRequest
            {
                JobId = jobId,
                DocumentType = documentType,
                DocumentUrl = documentUrl,
                FileName = fileName,
                CreatedAt = DateTime.UtcNow
            };

            // Store document if uploaded
            string? filePath = null;
            if (documentData != null && !string.IsNullOrEmpty(fileName))
            {
                using var stream = new MemoryStream(documentData);
                filePath = await _objectStorage.UploadFileAsync(
                    $"documents/{jobId}/{fileName}",
                    stream,
                    contentType ?? "application/octet-stream");
                jobRequest.FilePath = filePath;
            }

            // Create job status
            await _jobStatusRepository.CreateJobAsync(
                jobId,
                JobStatus.Pending,
                "Job created and queued for processing",
                CancellationToken.None);

            // Publish to queue
            await _messageProducer.PublishAsync("job-requests", jobRequest, CancellationToken.None);

            logger.LogInformation("Job {JobId} submitted successfully", jobId);

            // Return 202 Accepted
            var submitResponse = new SubmitJobResponse
            {
                JobId = jobId,
                StatusUrl = $"/api/v1/jobs/{jobId}/status"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(submitResponse));
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error submitting job");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new { error = "Internal server error" }));
            return errorResponse;
        }
    }

    [Function("GetJobStatus")]
    public async Task<HttpResponseData> GetJobStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/v1/jobs/{jobId}/status")] HttpRequestData req,
        string jobId,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger("GetJobStatus");
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            if (!await ValidateApiKeyAsync(req, logger))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await errorResponse.WriteStringAsync("Invalid or missing API key");
                return errorResponse;
            }

            var status = await _jobStatusRepository.GetJobStatusAsync(jobId);
            if (status == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteStringAsync(JsonSerializer.Serialize(new { error = "Job not found" }));
                return notFound;
            }

            await response.WriteStringAsync(JsonSerializer.Serialize(status));
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving job status for {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new { error = "Internal server error" }));
            return errorResponse;
        }
    }

    [Function("GetJobResults")]
    public async Task<HttpResponseData> GetJobResults(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/v1/jobs/{jobId}/results")] HttpRequestData req,
        string jobId,
        FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger("GetJobResults");
        
        try
        {
            if (!await ValidateApiKeyAsync(req, logger))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await errorResponse.WriteStringAsync("Invalid or missing API key");
                return errorResponse;
            }

            var status = await _jobStatusRepository.GetJobStatusAsync(jobId);
            if (status == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteStringAsync(JsonSerializer.Serialize(new { error = "Job not found" }));
                return notFound;
            }

            if (status.Status != JobStatus.Complete)
            {
                var accepted = req.CreateResponse(HttpStatusCode.Accepted);
                accepted.Headers.Add("Content-Type", "application/json");
                await accepted.WriteStringAsync(JsonSerializer.Serialize(new { message = "Job is still processing", status = status.Status.ToString() }));
                return accepted;
            }

            // Generate pre-signed URLs for results (valid for 1 hour)
            var expiration = TimeSpan.FromHours(1);
            var templateJsonUrl = await _objectStorage.GeneratePreSignedUrlAsync(
                $"results/{jobId}/template.json",
                expiration);
            var contextJsonUrl = await _objectStorage.GeneratePreSignedUrlAsync(
                $"results/{jobId}/context.json",
                expiration);

            var success = req.CreateResponse(HttpStatusCode.OK);
            success.Headers.Add("Content-Type", "application/json");
            await success.WriteStringAsync(JsonSerializer.Serialize(new
            {
                jobId,
                templateJsonUrl,
                contextJsonUrl
            }));
            return success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving job results for {JobId}", jobId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new { error = "Internal server error" }));
            return errorResponse;
        }
    }

    private async Task<bool> ValidateApiKeyAsync(HttpRequestData req, ILogger logger)
    {
        // Check for API key in header
        if (!req.Headers.TryGetValues("X-API-Key", out var apiKeyValues))
        {
            logger.LogWarning("Missing API key in request");
            return false;
        }

        var apiKey = apiKeyValues.FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("Empty API key in request");
            return false;
        }

        var isValid = await _userService.ValidateApiKeyAsync(apiKey, req.FunctionContext.CancellationToken);

        if (!isValid)
        {
            logger.LogWarning("Invalid API key attempted");
        }

        return isValid;
    }
}

