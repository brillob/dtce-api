using Azure;
using Azure.Data.Tables;
using Dtce.Common;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Dtce.Persistence;

public class AzureTableStorageJobRepository : IJobStatusRepository
{
    private readonly TableClient _tableClient;
    private readonly ILogger<AzureTableStorageJobRepository> _logger;
    private const string TableName = "JobStatus";

    public AzureTableStorageJobRepository(string connectionString, ILogger<AzureTableStorageJobRepository> logger)
    {
        var serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient(TableName);
        _tableClient.CreateIfNotExists();
        _logger = logger;
    }

    public async Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await _tableClient.GetEntityAsync<TableEntity>("Job", jobId, cancellationToken: cancellationToken);
            
            var completedAt = entity.Value.GetDateTimeOffset("CompletedAt");
            return new JobStatusResponse
            {
                JobId = entity.Value.GetString("JobId") ?? jobId,
                Status = Enum.Parse<JobStatus>(entity.Value.GetString("Status") ?? "Pending"),
                StatusMessage = entity.Value.GetString("StatusMessage") ?? string.Empty,
                CompletedAt = completedAt.HasValue ? completedAt.Value.DateTime : (DateTime?)null,
                ErrorMessage = entity.Value.GetString("ErrorMessage")
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task CreateJobAsync(string jobId, JobStatus status, string statusMessage, CancellationToken cancellationToken = default)
    {
        var entity = new TableEntity("Job", jobId)
        {
            ["JobId"] = jobId,
            ["Status"] = status.ToString(),
            ["StatusMessage"] = statusMessage,
            ["CreatedAt"] = DateTimeOffset.UtcNow,
            ["UpdatedAt"] = DateTimeOffset.UtcNow
        };

        await _tableClient.UpsertEntityAsync(entity, cancellationToken: cancellationToken);
        _logger.LogInformation("Created job {JobId} with status {Status}", jobId, status);
    }

    public async Task UpdateJobStatusAsync(string jobId, JobStatus status, string statusMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _tableClient.GetEntityAsync<TableEntity>("Job", jobId, cancellationToken: cancellationToken);
            var entity = existing.Value;
            
            entity["Status"] = status.ToString();
            entity["StatusMessage"] = statusMessage;
            entity["UpdatedAt"] = DateTimeOffset.UtcNow;

            await _tableClient.UpsertEntityAsync(entity, cancellationToken: cancellationToken);
            _logger.LogInformation("Updated job {JobId} to status {Status}", jobId, status);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            await CreateJobAsync(jobId, status, statusMessage, cancellationToken);
        }
    }

    public async Task UpdateJobCompletionAsync(string jobId, string templateJsonUrl, string contextJsonUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _tableClient.GetEntityAsync<TableEntity>("Job", jobId, cancellationToken: cancellationToken);
            var entity = existing.Value;
            
            entity["Status"] = JobStatus.Complete.ToString();
            entity["StatusMessage"] = "Job completed successfully";
            entity["CompletedAt"] = DateTimeOffset.UtcNow;
            entity["TemplateJsonUrl"] = templateJsonUrl;
            entity["ContextJsonUrl"] = contextJsonUrl;
            entity["UpdatedAt"] = DateTimeOffset.UtcNow;

            await _tableClient.UpsertEntityAsync(entity, cancellationToken: cancellationToken);
            _logger.LogInformation("Marked job {JobId} as complete", jobId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            await CreateJobAsync(jobId, JobStatus.Complete, "Job completed successfully", cancellationToken);
        }
    }

    public async Task UpdateJobErrorAsync(string jobId, string errorMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _tableClient.GetEntityAsync<TableEntity>("Job", jobId, cancellationToken: cancellationToken);
            var entity = existing.Value;
            
            entity["Status"] = JobStatus.Failed.ToString();
            entity["StatusMessage"] = "Job failed";
            entity["ErrorMessage"] = errorMessage;
            entity["UpdatedAt"] = DateTimeOffset.UtcNow;

            await _tableClient.UpsertEntityAsync(entity, cancellationToken: cancellationToken);
            _logger.LogError("Marked job {JobId} as failed: {Error}", jobId, errorMessage);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            await CreateJobAsync(jobId, JobStatus.Failed, "Job failed", cancellationToken);
        }
    }
}

