using Dtce.Common;
using Dtce.JobQueue;
using Dtce.Persistence;

namespace Dtce.ParsingEngine;

public class Worker : BackgroundService
{
    private readonly IMessageConsumer _messageConsumer;
    private readonly IJobStatusRepository _jobStatusRepository;
    private readonly IObjectStorage _objectStorage;
    private readonly IMessageProducer _messageProducer;
    private readonly IDocumentParser _documentParser;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IMessageConsumer messageConsumer,
        IJobStatusRepository jobStatusRepository,
        IObjectStorage objectStorage,
        IMessageProducer messageProducer,
        IDocumentParser documentParser,
        ILogger<Worker> logger)
    {
        _messageConsumer = messageConsumer;
        _jobStatusRepository = jobStatusRepository;
        _objectStorage = objectStorage;
        _messageProducer = messageProducer;
        _documentParser = documentParser;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Parsing Engine started");

        await _messageConsumer.StartConsumingAsync<JobRequest>(
            "parsing-jobs",
            async (jobRequest, ct) =>
            {
                try
                {
                    _logger.LogInformation("Parsing document for job {JobId}", jobRequest.JobId);

                    await _jobStatusRepository.UpdateJobStatusAsync(
                        jobRequest.JobId,
                        JobStatus.ParsingInProgress,
                        "Parsing document structure and extracting content",
                        ct);

                    // Parse document based on type
                    var parseResult = await _documentParser.ParseAsync(jobRequest, ct);

                    // Store parsed data
                    var parseResultKey = $"parsed/{jobRequest.JobId}/parse-result.json";
                    using var parseResultStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                        System.Text.Json.JsonSerializer.Serialize(parseResult)));
                    await _objectStorage.UploadFileAsync(parseResultKey, parseResultStream, "application/json", ct);

                    // Update status and send to analysis engine
                    await _jobStatusRepository.UpdateJobStatusAsync(
                        jobRequest.JobId,
                        JobStatus.AnalysisInProgress,
                        "Document parsed, sent to analysis engine",
                        ct);

                    // Create analysis job
                    var analysisJob = new AnalysisJob
                    {
                        JobId = jobRequest.JobId,
                        ParseResultKey = parseResultKey,
                        DocumentType = jobRequest.DocumentType
                    };

                    await _messageProducer.PublishAsync("analysis-jobs", analysisJob, ct);

                    _logger.LogInformation("Job {JobId} sent to analysis engine", jobRequest.JobId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing document for job {JobId}", jobRequest.JobId);
                    await _jobStatusRepository.UpdateJobErrorAsync(
                        jobRequest.JobId,
                        $"Parsing error: {ex.Message}",
                        ct);
                }
            },
            stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
