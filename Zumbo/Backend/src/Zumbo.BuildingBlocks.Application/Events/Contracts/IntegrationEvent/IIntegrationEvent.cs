namespace Zumbo.BuildingBlocks.Application.Events;

public interface IIntegrationEvent
{
    string EventId { get; }
    string EventName { get; }
    string AggregateId { get; }
    DateTimeOffset OccurredAt { get; }
}
