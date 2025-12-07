namespace Dtce.JobQueue;

public interface IMessageConsumer
{
    Task StartConsumingAsync<T>(string topic, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class;
    Task StopConsumingAsync(CancellationToken cancellationToken = default);
}

