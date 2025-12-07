using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Dtce.JobQueue;

public class AzureServiceBusConsumer : IMessageConsumer, IDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<AzureServiceBusConsumer> _logger;
    private readonly Dictionary<string, ServiceBusProcessor> _processors = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public AzureServiceBusConsumer(string connectionString, ILogger<AzureServiceBusConsumer> logger)
    {
        _client = new ServiceBusClient(connectionString);
        _logger = logger;
    }

    public async Task StartConsumingAsync<T>(string topic, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class
    {
        if (_processors.ContainsKey(topic))
        {
            _logger.LogWarning("Already consuming from topic {Topic}", topic);
            return;
        }

        var processor = _client.CreateProcessor(topic, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1
        });

        processor.ProcessMessageAsync += async args =>
        {
            try
            {
                var messageBody = Encoding.UTF8.GetString(args.Message.Body.ToArray());
                var message = JsonSerializer.Deserialize<T>(messageBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (message != null)
                {
                    await handler(message, args.CancellationToken);
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                }
                else
                {
                    _logger.LogWarning("Failed to deserialize message from topic {Topic}", topic);
                    await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from topic {Topic}", topic);
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
            }
        };

        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Error in processor for topic {Topic}", topic);
            return Task.CompletedTask;
        };

        _processors[topic] = processor;
        await processor.StartProcessingAsync(cancellationToken);
        _logger.LogInformation("Started consuming from topic {Topic}", topic);
    }

    public async Task StopConsumingAsync(CancellationToken cancellationToken = default)
    {
        _cancellationTokenSource.Cancel();
        
        foreach (var processor in _processors.Values)
        {
            await processor.StopProcessingAsync(cancellationToken);
            await processor.DisposeAsync();
        }
        
        _processors.Clear();
    }

    public void Dispose()
    {
        StopConsumingAsync().Wait();
        _client?.DisposeAsync().AsTask().Wait();
        _cancellationTokenSource?.Dispose();
    }
}

