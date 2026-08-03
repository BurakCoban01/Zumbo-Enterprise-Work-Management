namespace Zumbo.BuildingBlocks.Application.Events;

public interface IIntegrationEventMapper<in TDomainEvent, out TIntegrationEvent>
    where TIntegrationEvent : IIntegrationEvent
{
    TIntegrationEvent Map(TDomainEvent domainEvent);
}
