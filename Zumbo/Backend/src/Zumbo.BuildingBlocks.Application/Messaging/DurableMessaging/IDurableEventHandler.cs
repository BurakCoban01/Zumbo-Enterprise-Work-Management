namespace Zumbo.BuildingBlocks.Application.Messaging;

public interface IDurableEventHandler
{
    string ConsumerName { get; }
    string EventType { get; }
    Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken);
}
