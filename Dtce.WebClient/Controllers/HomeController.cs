using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dtce.WebClient.Models;
using Dtce.Common;
using Dtce.Identity;
using Dtce.Identity.Models;
using Dtce.WebClient.Services;
using System.Security.Claims;

namespace Dtce.WebClient.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly DtceApiService _apiService;
    private readonly IUserService _userService;
    private readonly JobHistoryService _historyService;

    public HomeController(ILogger<HomeController> logger, DtceApiService apiService, IUserService userService, JobHistoryService historyService)
    {
        _logger = logger;
        _apiService = apiService;
        _userService = userService;
        _historyService = historyService;
    }

    public async Task<IActionResult> Index()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var apiKeys = await _userService.GetUserApiKeysAsync(userId);
        var model = new JobViewModel
        {
            ApiKeys = apiKeys
        };
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SubmitJob(IFormFile? document, string? documentUrl, string inputType, string? apiKey)
    {
        try
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                return Json(new { success = false, message = "Please select an API key" });
            }

            IFormFile? fileToSubmit = null;
            string? urlToSubmit = null;

            if (inputType == "file" && document != null)
            {
                fileToSubmit = document;
            }
            else if (inputType == "url" && !string.IsNullOrWhiteSpace(documentUrl))
            {
                urlToSubmit = documentUrl;
            }
            else
            {
                return Json(new { success = false, message = "Please provide either a file or a URL" });
            }

            var response = await _apiService.SubmitJobAsync(fileToSubmit, urlToSubmit, apiKey);
            
            if (response != null)
            {
                // Save job history
                var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var history = new Models.JobHistory
                {
                    JobId = response.JobId,
                    UserId = userId.ToString(),
                    FileName = fileToSubmit?.FileName,
                    DocumentUrl = urlToSubmit,
                    InputType = inputType,
                    Status = "Pending",
                    StatusMessage = "Job created and queued for processing",
                    SubmittedAt = DateTime.UtcNow
                };
                await _historyService.SaveJobHistoryAsync(history);

                return Json(new { success = true, jobId = response.JobId });
            }

            _logger.LogError("SubmitJobAsync returned null - check API Gateway connection and logs");
            return Json(new { success = false, message = "Failed to submit job. Please check if the API Gateway is running on http://localhost:5017" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting job");
            return Json(new { success = false, message = "An error occurred" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetJobStatus(string jobId, string? apiKey)
    {
        try
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                return Json(new { error = "API key is required" });
            }

            var status = await _apiService.GetJobStatusAsync(jobId, apiKey);
            if (status != null)
            {
                string? templateUrl = null;
                string? contextUrl = null;

                if (status.Status == JobStatus.Complete)
                {
                    var results = await _apiService.GetJobResultsAsync(jobId, apiKey);
                    if (results is { IsPending: false })
                    {
                        templateUrl = results.TemplateJsonUrl;
                        contextUrl = results.ContextJsonUrl;
                    }
                }

                // Update job history
                var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await _historyService.UpdateJobHistoryAsync(jobId, userId.ToString(), history =>
                {
                    history.Status = status.Status.ToString();
                    history.StatusMessage = status.StatusMessage;
                    history.TemplateJsonUrl = templateUrl;
                    history.ContextJsonUrl = contextUrl;
                    if (status.Status == JobStatus.Complete && history.CompletedAt == null)
                    {
                        history.CompletedAt = DateTime.UtcNow;
                    }
                });

                return Json(new
                {
                    jobId = status.JobId,
                    status = status.Status.ToString(),
                    statusMessage = status.StatusMessage,
                    templateJsonUrl = templateUrl,
                    contextJsonUrl = contextUrl
                });
            }

            return Json(new { jobId, status = "NotFound" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job status");
            return Json(new { error = "An error occurred" });
        }
    }

    public async Task<IActionResult> History()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var history = await _historyService.GetUserJobHistoryAsync(userId.ToString());
        return View(history);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
