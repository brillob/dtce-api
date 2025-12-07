using Dtce.Infrastructure.Local;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dtce.Tests;

public class FileQueueMessagingTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"dtce-filequeue-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ProducerAndConsumer_WorkTogetherToDeliverMessages()
    {
        Directory.CreateDirectory(_rootPath);
        var options = Options.Create(new FileQueueOptions
        {
            RootPath = _rootPath,
            PollInterval = TimeSpan.FromMilliseconds(10)
        });

        var producer = new FileQueueMessageProducer(options, NullLogger<FileQueueMessageProducer>.Instance);
        var consumer = new FileQueueMessageConsumer(options, NullLogger<FileQueueMessageConsumer>.Instance);

        var tcs = new TaskCompletionSource<TestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await consumer.StartConsumingAsync<TestMessage>(
            "analysis-jobs",
            (message, _) =>
            {
                tcs.TrySetResult(message);
                return Task.CompletedTask;
            });

        await producer.PublishAsync("analysis-jobs", new TestMessage("ready"));

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        received.Value.Should().Be("ready");

        await consumer.StopConsumingAsync();
    }

    private record TestMessage(string Value);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }
}


