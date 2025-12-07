using Dtce.Common;

namespace Dtce.Persistence;

public interface IJobStatusRepository
{
    Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default);
    Task CreateJobAsync(string jobId, JobStatus status, string statusMessage, CancellationToken cancellationToken = default);
    Task UpdateJobStatusAsync(string jobId, JobStatus status, string statusMessage, CancellationToken cancellationToken = default);
    Task UpdateJobCompletionAsync(string jobId, string templateJsonUrl, string contextJsonUrl, CancellationToken cancellationToken = default);
    Task UpdateJobErrorAsync(string jobId, string errorMessage, CancellationToken cancellationToken = default);
}

