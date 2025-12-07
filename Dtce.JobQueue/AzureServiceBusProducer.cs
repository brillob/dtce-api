using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Dtce.JobQueue;

public class AzureServiceBusProducer : IMessageProducer
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<AzureServiceBusProducer> _logger;

    public AzureServiceBusProducer(string connectionString, ILogger<AzureServiceBusProducer> logger)
    {
        _client = new ServiceBusClient(connectionString);
        _logger = logger;
    }

    public async Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken = default) where T : class
    {
        await using var sender = _client.CreateSender(topic);
        
        var messageBody = JsonSerializer.Serialize(message);
        var serviceBusMessage = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
        {
            ContentType = "application/json",
            Subject = typeof(T).Name
        };

        await sender.SendMessageAsync(serviceBusMessage, cancellationToken);
        _logger.LogInformation("Published message to topic {Topic}: {MessageType}", topic, typeof(T).Name);
    }

    public void Dispose()
    {
        _client?.DisposeAsync().AsTask().Wait();
    }
}

