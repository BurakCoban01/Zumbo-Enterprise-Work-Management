namespace Zumbo.BuildingBlocks.Application.Events;

public interface IIntegrationEvent
{
    string EventId { get; }
    string EventName { get; }
    string AggregateId { get; }
    DateTimeOffset OccurredAt { get; }
}

public interface IIntegrationEventMapper<in TDomainEvent, out TIntegrationEvent>
    where TIntegrationEvent : IIntegrationEvent
{
    TIntegrationEvent Map(TDomainEvent domainEvent);
}
