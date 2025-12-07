using System.Text.Json;
using Dtce.Common;
using Dtce.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dtce.Infrastructure.Local;

public class FileSystemJobStatusRepository : IJobStatusRepository
{
    private readonly ILogger<FileSystemJobStatusRepository> _logger;
    private readonly string _jobsDirectory;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public FileSystemJobStatusRepository(
        IOptions<FileSystemStorageOptions> options,
        ILogger<FileSystemJobStatusRepository> logger)
    {
        _logger = logger;
        _jobsDirectory = Path.Combine(options.Value.RootPath, "jobs");
        Directory.CreateDirectory(_jobsDirectory);
    }

    public async Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var path = GetJobPath(jobId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var document = await JsonSerializer.DeserializeAsync<JobStatusDocument>(stream, _serializerOptions, cancellationToken);
        return document?.Status;
    }

    public async Task CreateJobAsync(string jobId, JobStatus status, string statusMessage, CancellationToken cancellationToken = default)
    {
        var document = new JobStatusDocument
        {
            Status = new JobStatusResponse
            {
                JobId = jobId,
                Status = status,
                StatusMessage = statusMessage
            }
        };
        await SaveJob(jobId, document, cancellationToken);
    }

    public async Task UpdateJobStatusAsync(string jobId, JobStatus status, string statusMessage, CancellationToken cancellationToken = default)
    {
        var document = await LoadOrCreate(jobId, cancellationToken);
        document.Status.Status = status;
        document.Status.StatusMessage = statusMessage;
        await SaveJob(jobId, document, cancellationToken);
    }

    public async Task UpdateJobCompletionAsync(string jobId, string templateJsonUrl, string contextJsonUrl, CancellationToken cancellationToken = default)
    {
        var document = await LoadOrCreate(jobId, cancellationToken);
        document.Status.Status = JobStatus.Complete;
        document.Status.StatusMessage = "Job completed successfully";
        document.Status.CompletedAt = DateTime.UtcNow;
        document.TemplateJsonUrl = templateJsonUrl;
        document.ContextJsonUrl = contextJsonUrl;
        await SaveJob(jobId, document, cancellationToken);
    }

    public async Task UpdateJobErrorAsync(string jobId, string errorMessage, CancellationToken cancellationToken = default)
    {
        var document = await LoadOrCreate(jobId, cancellationToken);
        document.Status.Status = JobStatus.Failed;
        document.Status.StatusMessage = "Job failed";
        document.Status.ErrorMessage = errorMessage;
        document.Status.CompletedAt ??= DateTime.UtcNow;
        await SaveJob(jobId, document, cancellationToken);
    }

    private async Task<JobStatusDocument> LoadOrCreate(string jobId, CancellationToken cancellationToken)
    {
        var path = GetJobPath(jobId);
        if (!File.Exists(path))
        {
            return new JobStatusDocument
            {
                Status = new JobStatusResponse { JobId = jobId }
            };
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var document = await JsonSerializer.DeserializeAsync<JobStatusDocument>(stream, _serializerOptions, cancellationToken);
        return document ?? new JobStatusDocument { Status = new JobStatusResponse { JobId = jobId } };
    }

    private async Task SaveJob(string jobId, JobStatusDocument document, CancellationToken cancellationToken)
    {
        var path = GetJobPath(jobId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, document, _serializerOptions, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }

        _logger.LogInformation("Persisted job status for {JobId} ({Status})", jobId, document.Status.Status);
    }

    private string GetJobPath(string jobId)
    {
        var safeId = string.Join("_", jobId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return Path.Combine(_jobsDirectory, $"{safeId}.json");
    }

    private sealed class JobStatusDocument
    {
        public JobStatusResponse Status { get; set; } = new();
        public string? TemplateJsonUrl { get; set; }
        public string? ContextJsonUrl { get; set; }
    }
}


