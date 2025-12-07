using Dtce.Common;
using System.Text;
using System.Text.Json;

namespace Dtce.WebClient.Services;

public class DtceApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DtceApiService> _logger;
    private readonly string _apiBaseUrl;

    public DtceApiService(HttpClient httpClient, IConfiguration configuration, ILogger<DtceApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiBaseUrl = configuration["ApiSettings:BaseUrl"] ?? "https://localhost:7000";
    }

    public async Task<SubmitJobResponse?> SubmitJobAsync(IFormFile? document, string? documentUrl, string? apiKey = null)
    {
        try
        {
            using var content = new MultipartFormDataContent();

            if (document != null)
            {
                var fileContent = new StreamContent(document.OpenReadStream());
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(document.ContentType ?? "application/octet-stream");
                content.Add(fileContent, "document", document.FileName);
            }

            if (!string.IsNullOrWhiteSpace(documentUrl))
            {
                content.Add(new StringContent(documentUrl), "documentUrl");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUrl}/api/v1/jobs/submit")
            {
                Content = content
            };

            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Add("X-API-Key", apiKey);
            }

            var response = await _httpClient.SendAsync(request);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SubmitJobResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to submit job: {StatusCode} - {Error}", response.StatusCode, errorContent);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting job");
            return null;
        }
    }

    public async Task<JobStatusResponse?> GetJobStatusAsync(string jobId, string? apiKey = null)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiBaseUrl}/api/v1/jobs/{jobId}/status");
            
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Add("X-API-Key", apiKey);
            }

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<JobStatusResponse>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job status for {JobId}", jobId);
            return null;
        }
    }

    public async Task<JobResults?> GetJobResultsAsync(string jobId, string apiKey)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiBaseUrl}/api/v1/jobs/{jobId}/results");
            request.Headers.Add("X-API-Key", apiKey);

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                return JobResults.Pending;
            }

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var document = JsonDocument.Parse(responseContent);

                var templateUrl = document.RootElement.GetProperty("templateJsonUrl").GetString();
                var contextUrl = document.RootElement.GetProperty("contextJsonUrl").GetString();
                return new JobResults(templateUrl, contextUrl, false);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job results for {JobId}", jobId);
            return null;
        }
    }
}

public record JobResults(string? TemplateJsonUrl, string? ContextJsonUrl, bool IsPending)
{
    public static JobResults Pending { get; } = new(null, null, true);
}

