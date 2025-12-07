using Dtce.Common;
using Dtce.Common.Models;
using Dtce.JobQueue;
using Dtce.Persistence;
using System.Text.Json;
using Dtce.AnalysisEngine;

namespace Dtce.AnalysisEngine;

public class Worker : BackgroundService
{
    private readonly IMessageConsumer _messageConsumer;
    private readonly IJobStatusRepository _jobStatusRepository;
    private readonly IObjectStorage _objectStorage;
    private readonly INlpAnalyzer _nlpAnalyzer;
    private readonly IComputerVisionAnalyzer _cvAnalyzer;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IMessageConsumer messageConsumer,
        IJobStatusRepository jobStatusRepository,
        IObjectStorage objectStorage,
        INlpAnalyzer nlpAnalyzer,
        IComputerVisionAnalyzer cvAnalyzer,
        ILogger<Worker> logger)
    {
        _messageConsumer = messageConsumer;
        _jobStatusRepository = jobStatusRepository;
        _objectStorage = objectStorage;
        _nlpAnalyzer = nlpAnalyzer;
        _cvAnalyzer = cvAnalyzer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Analysis Engine started");

        await _messageConsumer.StartConsumingAsync<AnalysisJob>(
            "analysis-jobs",
            async (analysisJob, ct) =>
            {
                try
                {
                    _logger.LogInformation("Analyzing document for job {JobId}", analysisJob.JobId);

                    await _jobStatusRepository.UpdateJobStatusAsync(
                        analysisJob.JobId,
                        JobStatus.AnalysisInProgress,
                        "Performing NLP and CV analysis",
                        ct);

                    // Load parse result
                    using var parseResultStream = await _objectStorage.DownloadFileAsync(analysisJob.ParseResultKey, ct);
                    var parseResult = await JsonSerializer.DeserializeAsync<ParseResult>(
                        parseResultStream,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                        ct);

                    if (parseResult == null)
                    {
                        throw new InvalidOperationException("Failed to deserialize parse result");
                    }

                    // Perform NLP analysis
                    var allText = string.Join(" ", parseResult.ContentSections.Select(s => s.SampleText));
                    var nlpResult = await _nlpAnalyzer.AnalyzeAsync(allText, ct);

                    // Perform CV analysis (logo detection)
                    var cvResult = await _cvAnalyzer.DetectLogosAsync(parseResult, ct);

                    // Build final Template JSON
                    var templateJson = parseResult.TemplateJson;
                    templateJson.LogoMap = cvResult;

                    // Build final Context JSON
                    var contextJson = new ContextJson
                    {
                        LinguisticStyle = new LinguisticStyleAttributes
                        {
                            OverallFormality = nlpResult.Formality,
                            FormalityConfidenceScore = nlpResult.FormalityConfidence,
                            DominantTone = nlpResult.Tone,
                            ToneConfidenceScore = nlpResult.ToneConfidence,
                            WritingStyleVector = nlpResult.StyleVector
                        },
                        ContentBlocks = parseResult.ContentSections.Select(s => new ContentBlock
                        {
                            PlaceholderId = s.PlaceholderId,
                            SectionSampleText = s.SampleText,
                            WordCount = s.WordCount
                        }).ToList()
                    };

                    // Store final JSON files
                    var templateJsonKey = $"results/{analysisJob.JobId}/template.json";
                    var contextJsonKey = $"results/{analysisJob.JobId}/context.json";

                    using var templateStream = new MemoryStream(
                        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(templateJson)));
                    using var contextStream = new MemoryStream(
                        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(contextJson)));

                    await _objectStorage.UploadFileAsync(templateJsonKey, templateStream, "application/json", ct);
                    await _objectStorage.UploadFileAsync(contextJsonKey, contextStream, "application/json", ct);

                    // Update job status to complete
                    await _jobStatusRepository.UpdateJobCompletionAsync(
                        analysisJob.JobId,
                        templateJsonKey,
                        contextJsonKey,
                        ct);

                    _logger.LogInformation("Job {JobId} completed successfully", analysisJob.JobId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error analyzing document for job {JobId}", analysisJob.JobId);
                    await _jobStatusRepository.UpdateJobErrorAsync(
                        analysisJob.JobId,
                        $"Analysis error: {ex.Message}",
                        ct);
                }
            },
            stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
