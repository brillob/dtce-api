using System.Text.Json;
using Dtce.JobQueue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dtce.Infrastructure.Local;

public class FileQueueMessageProducer : IMessageProducer
{
    private readonly ILogger<FileQueueMessageProducer> _logger;
    private readonly FileQueueOptions _options;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public FileQueueMessageProducer(
        IOptions<FileQueueOptions> options,
        ILogger<FileQueueMessageProducer> logger)
    {
        _logger = logger;
        _options = options.Value;
        Directory.CreateDirectory(_options.RootPath);
    }

    public async Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic cannot be empty.", nameof(topic));
        }

        var topicPath = Path.Combine(_options.RootPath, SanitizeTopic(topic));
        Directory.CreateDirectory(topicPath);

        var payload = JsonSerializer.Serialize(message, SerializerOptions);
        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
        var filePath = Path.Combine(topicPath, fileName);

        await File.WriteAllTextAsync(filePath, payload, cancellationToken);
        _logger.LogInformation("Queued message for topic {Topic} at {FilePath}", topic, filePath);
    }

    private static string SanitizeTopic(string topic) =>
        string.Join("_", topic.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}


