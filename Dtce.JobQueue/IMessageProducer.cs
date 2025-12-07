namespace Dtce.JobQueue;

public interface IMessageProducer
{
    Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken = default) where T : class;
}

