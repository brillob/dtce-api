using Dtce.ApiGateway.Filters;
using Dtce.Common;
using Dtce.JobQueue;
using Dtce.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Dtce.ApiGateway.Controllers;

[ApiController]
[ApiKeyAuthorize]
[Route("api/v1/jobs")]
public class JobsController : ControllerBase
{
    private readonly IMessageProducer _messageProducer;
    private readonly IJobStatusRepository _jobStatusRepository;
    private readonly IObjectStorage _objectStorage;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IMessageProducer messageProducer,
        IJobStatusRepository jobStatusRepository,
        IObjectStorage objectStorage,
        ILogger<JobsController> logger)
    {
        _messageProducer = messageProducer;
        _jobStatusRepository = jobStatusRepository;
        _objectStorage = objectStorage;
        _logger = logger;
    }

    [HttpPost("submit")]
    [ProducesResponseType(typeof(SubmitJobResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitJob([FromForm] IFormFile? document, [FromForm] string? documentUrl)
    {
        try
        {
            // Validation
            if (document == null && string.IsNullOrWhiteSpace(documentUrl))
            {
                return BadRequest(new { error = "Either document file or document_url must be provided" });
            }

            if (document != null)
            {
                // Validate file type
                var allowedExtensions = new[] { ".docx", ".pdf" };
                var fileExtension = Path.GetExtension(document.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    return BadRequest(new { error = "Invalid file type. Only .docx and .pdf are supported" });
                }

                // Validate file size (50MB limit)
                const long maxFileSize = 50 * 1024 * 1024; // 50MB
                if (document.Length > maxFileSize)
                {
                    return BadRequest(new { error = "File size exceeds 50MB limit" });
                }
            }

            if (!string.IsNullOrWhiteSpace(documentUrl))
            {
                // Basic URL validation
                if (!Uri.TryCreate(documentUrl, UriKind.Absolute, out var uri) || 
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return BadRequest(new { error = "Invalid document URL" });
                }
            }

            // Generate job ID
            var jobId = Guid.NewGuid().ToString();

            // Determine document type
            DocumentType documentType;
            string? fileName = null;
            if (document != null)
            {
                var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
                documentType = extension == ".docx" ? DocumentType.Docx : DocumentType.Pdf;
                fileName = document.FileName;
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
            if (document != null)
            {
                using var stream = document.OpenReadStream();
                filePath = await _objectStorage.UploadFileAsync(
                    $"documents/{jobId}/{document.FileName}",
                    stream,
                    document.ContentType ?? "application/octet-stream");
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

            _logger.LogInformation("Job {JobId} submitted successfully", jobId);

            // Return 202 Accepted
            var response = new SubmitJobResponse
            {
                JobId = jobId,
                StatusUrl = $"/api/v1/jobs/{jobId}/status"
            };

            return Accepted(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting job");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("{jobId}/status")]
    [ProducesResponseType(typeof(JobStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobStatus(string jobId)
    {
        try
        {
            var status = await _jobStatusRepository.GetJobStatusAsync(jobId);
            if (status == null)
            {
                return NotFound(new { error = "Job not found" });
            }

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job status for {JobId}", jobId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("{jobId}/results")]
    [ProducesResponseType(StatusCodes.Status303SeeOther)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GetJobResults(string jobId, [FromQuery] bool includeContent = false)
    {
        try
        {
            var status = await _jobStatusRepository.GetJobStatusAsync(jobId);
            if (status == null)
            {
                return NotFound(new { error = "Job not found" });
            }

            if (status.Status != JobStatus.Complete)
            {
                return Accepted(new { message = "Job is still processing", status = status.Status.ToString() });
            }

            // Generate pre-signed URLs for results (valid for 1 hour)
            var expiration = TimeSpan.FromHours(1);
            var templateJsonUrl = await _objectStorage.GeneratePreSignedUrlAsync(
                $"results/{jobId}/template.json",
                expiration);
            var contextJsonUrl = await _objectStorage.GeneratePreSignedUrlAsync(
                $"results/{jobId}/context.json",
                expiration);

            // If includeContent is true, return the JSON content directly in the response
            if (includeContent)
            {
                using var templateStream = await _objectStorage.DownloadFileAsync($"results/{jobId}/template.json");
                using var contextStream = await _objectStorage.DownloadFileAsync($"results/{jobId}/context.json");
                
                using var templateReader = new StreamReader(templateStream);
                using var contextReader = new StreamReader(contextStream);
                
                var templateJson = await templateReader.ReadToEndAsync();
                var contextJson = await contextReader.ReadToEndAsync();

                return Ok(new
                {
                    jobId,
                    templateJsonUrl,
                    contextJsonUrl,
                    templateJson = System.Text.Json.JsonSerializer.Deserialize<object>(templateJson),
                    contextJson = System.Text.Json.JsonSerializer.Deserialize<object>(contextJson)
                });
            }

            // Default: return URLs only (for Web UI)
            return Ok(new
            {
                jobId,
                templateJsonUrl,
                contextJsonUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job results for {JobId}", jobId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("files/{*fileKey}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(string fileKey, [FromQuery] bool download = false)
    {
        try
        {
            // URL decode the file key (ASP.NET Core may have already decoded it, but be safe)
            fileKey = Uri.UnescapeDataString(fileKey);
            _logger.LogInformation("Requested file key: {FileKey}, download: {Download}", fileKey, download);
            
            var stream = await _objectStorage.DownloadFileAsync(fileKey);
            
            // Determine content type based on file extension
            var contentType = "application/octet-stream";
            if (fileKey.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "application/json";
            }
            else if (fileKey.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            }
            else if (fileKey.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "application/pdf";
            }

            var fileName = Path.GetFileName(fileKey);
            
            // For JSON files, don't set Content-Disposition header when viewing inline
            // Browsers will display JSON inline by default when Content-Type is application/json
            // Only set Content-Disposition for downloads or non-JSON files
            if (download)
            {
                // Force download
                Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");
                return File(stream, contentType, fileName);
            }
            else if (fileKey.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                // For JSON files, don't set Content-Disposition - browser will display inline
                return File(stream, contentType);
            }
            else
            {
                // For other file types, set inline with filename
                Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
                return File(stream, contentType);
            }
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "File not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving file {FileKey}", fileKey);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

