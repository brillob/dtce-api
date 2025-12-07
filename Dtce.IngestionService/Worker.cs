using Dtce.Common;
using Dtce.JobQueue;
using Dtce.Persistence;

namespace Dtce.IngestionService;

public class Worker : BackgroundService
{
    private readonly IMessageConsumer _messageConsumer;
    private readonly IJobStatusRepository _jobStatusRepository;
    private readonly IObjectStorage _objectStorage;
    private readonly IMessageProducer _messageProducer;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IMessageConsumer messageConsumer,
        IJobStatusRepository jobStatusRepository,
        IObjectStorage objectStorage,
        IMessageProducer messageProducer,
        ILogger<Worker> logger)
    {
        _messageConsumer = messageConsumer;
        _jobStatusRepository = jobStatusRepository;
        _objectStorage = objectStorage;
        _messageProducer = messageProducer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Ingestion Service started");

        // Start consuming job requests
        await _messageConsumer.StartConsumingAsync<JobRequest>(
            "job-requests",
            async (jobRequest, ct) =>
            {
                try
                {
                    _logger.LogInformation("Processing job request {JobId}", jobRequest.JobId);

                    // Update status to Processing
                    await _jobStatusRepository.UpdateJobStatusAsync(
                        jobRequest.JobId,
                        JobStatus.Processing,
                        "Document ingestion in progress",
                        ct);

                    // If document URL is provided, fetch and store it
                    if (!string.IsNullOrEmpty(jobRequest.DocumentUrl))
                    {
                        _logger.LogInformation("Fetching document from URL: {Url}", jobRequest.DocumentUrl);
                        // In a real implementation, fetch from URL and store
                        // For now, just log
                    }

                    // Validate document exists (if file path provided)
                    if (!string.IsNullOrEmpty(jobRequest.FilePath))
                    {
                        try
                        {
                            await _objectStorage.DownloadFileAsync(jobRequest.FilePath, ct);
                            _logger.LogInformation("Document validated: {FilePath}", jobRequest.FilePath);
                        }
                        catch (FileNotFoundException)
                        {
                            _logger.LogError("Document not found: {FilePath}", jobRequest.FilePath);
                            await _jobStatusRepository.UpdateJobErrorAsync(
                                jobRequest.JobId,
                                "Document file not found",
                                ct);
                            return;
                        }
                    }

                    // Update status and publish to parsing queue
                    await _jobStatusRepository.UpdateJobStatusAsync(
                        jobRequest.JobId,
                        JobStatus.ParsingInProgress,
                        "Document validated, sent to parsing engine",
                        ct);

                    // Publish to parsing queue
                    await _messageProducer.PublishAsync("parsing-jobs", jobRequest, ct);

                    _logger.LogInformation("Job {JobId} sent to parsing engine", jobRequest.JobId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing job {JobId}", jobRequest.JobId);
                    await _jobStatusRepository.UpdateJobErrorAsync(
                        jobRequest.JobId,
                        $"Ingestion error: {ex.Message}",
                        ct);
                }
            },
            stoppingToken);

        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
