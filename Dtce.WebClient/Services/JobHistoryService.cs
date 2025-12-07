using System.Text.Json;
using Dtce.WebClient.Models;
using Microsoft.Extensions.Logging;

namespace Dtce.WebClient.Services;

public class JobHistoryService
{
    private readonly ILogger<JobHistoryService> _logger;
    private readonly string _historyDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public JobHistoryService(ILogger<JobHistoryService> logger, IConfiguration configuration)
    {
        _logger = logger;
        var historyPath = configuration["JobHistory:StoragePath"] 
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DtceWebClient", "history");
        _historyDirectory = Path.GetFullPath(historyPath);
        Directory.CreateDirectory(_historyDirectory);
    }

    public async Task SaveJobHistoryAsync(JobHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var userHistoryDir = Path.Combine(_historyDirectory, history.UserId);
            Directory.CreateDirectory(userHistoryDir);

            var filePath = Path.Combine(userHistoryDir, $"{history.JobId}.json");
            var json = JsonSerializer.Serialize(history, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);
            
            _logger.LogInformation("Saved job history for {JobId}", history.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving job history for {JobId}", history.JobId);
        }
    }

    public async Task UpdateJobHistoryAsync(string jobId, string userId, Action<JobHistory> updateAction, CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = Path.Combine(_historyDirectory, userId, $"{jobId}.json");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Job history not found for {JobId}", jobId);
                return;
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var history = JsonSerializer.Deserialize<JobHistory>(json, JsonOptions);
            
            if (history != null)
            {
                updateAction(history);
                var updatedJson = JsonSerializer.Serialize(history, JsonOptions);
                await File.WriteAllTextAsync(filePath, updatedJson, cancellationToken);
                _logger.LogInformation("Updated job history for {JobId}", jobId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating job history for {JobId}", jobId);
        }
    }

    public async Task<List<JobHistory>> GetUserJobHistoryAsync(string userId, int? limit = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var userHistoryDir = Path.Combine(_historyDirectory, userId);
            if (!Directory.Exists(userHistoryDir))
            {
                return new List<JobHistory>();
            }

            var files = Directory.GetFiles(userHistoryDir, "*.json")
                .OrderByDescending(f => File.GetCreationTime(f))
                .ToList();

            if (limit.HasValue)
            {
                files = files.Take(limit.Value).ToList();
            }

            var historyList = new List<JobHistory>();
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var history = JsonSerializer.Deserialize<JobHistory>(json, JsonOptions);
                    if (history != null)
                    {
                        historyList.Add(history);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading history file {File}", file);
                }
            }

            return historyList.OrderByDescending(h => h.SubmittedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job history for user {UserId}", userId);
            return new List<JobHistory>();
        }
    }

    public async Task<JobHistory?> GetJobHistoryAsync(string jobId, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = Path.Combine(_historyDirectory, userId, $"{jobId}.json");
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<JobHistory>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving job history for {JobId}", jobId);
            return null;
        }
    }
}

