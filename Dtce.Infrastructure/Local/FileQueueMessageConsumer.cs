using System.Text.Json;
using Dtce.JobQueue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dtce.Infrastructure.Local;

public class FileQueueMessageConsumer : IMessageConsumer
{
    private readonly ILogger<FileQueueMessageConsumer> _logger;
    private readonly FileQueueOptions _options;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private CancellationTokenSource? _linkedCts;
    private Task? _processingTask;

    public FileQueueMessageConsumer(
        IOptions<FileQueueOptions> options,
        ILogger<FileQueueMessageConsumer> logger)
    {
        _logger = logger;
        _options = options.Value;
        Directory.CreateDirectory(_options.RootPath);
    }

    public Task StartConsumingAsync<T>(string topic, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        if (_processingTask != null)
        {
            throw new InvalidOperationException("Consumer is already running.");
        }

        var topicPath = Path.Combine(_options.RootPath, SanitizeTopic(topic));
        Directory.CreateDirectory(topicPath);

        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = Task.Run(() => ProcessLoop(topicPath, handler, _linkedCts.Token), CancellationToken.None);

        _logger.LogInformation("Started file queue consumer for topic {Topic} (path {Path})", topic, topicPath);
        return Task.CompletedTask;
    }

    public async Task StopConsumingAsync(CancellationToken cancellationToken = default)
    {
        if (_processingTask == null)
        {
            return;
        }

        _linkedCts?.Cancel();

        try
        {
            await _processingTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
        finally
        {
            _processingTask = null;
            _linkedCts?.Dispose();
            _linkedCts = null;
        }
    }

    private async Task ProcessLoop<T>(string topicPath, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken) where T : class
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var files = Directory.GetFiles(topicPath, "*.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        await using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, useAsync: true);
                        using var reader = new StreamReader(stream);
                        var payload = await reader.ReadToEndAsync();
                        var message = JsonSerializer.Deserialize<T>(payload, _serializerOptions);
                        if (message == null)
                        {
                            _logger.LogWarning("Skipping malformed message file {File}", file);
                        }
                        else
                        {
                            await handler(message, cancellationToken);
                        }
                    }
                    catch (IOException ex)
                    {
                        _logger.LogDebug(ex, "Could not process file {File}, it may be locked by another consumer.", file);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing queue item {File}", file);
                    }
                    finally
                    {
                        TryDelete(file);
                    }
                }

                await Task.Delay(_options.PollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in file queue processing loop.");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    private static string SanitizeTopic(string topic) =>
        string.Join("_", topic.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete processed queue file {File}", file);
        }
    }
}


