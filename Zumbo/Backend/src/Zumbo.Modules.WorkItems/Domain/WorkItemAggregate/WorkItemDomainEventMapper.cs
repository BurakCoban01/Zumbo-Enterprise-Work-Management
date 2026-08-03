using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemDomainEventMapper :
    IIntegrationEventMapper<WorkItemPlanningChangedDomainEvent, WorkItemPlanningChangedIntegrationEvent>,
    IIntegrationEventMapper<WorkItemMovedDomainEvent, WorkItemMovedIntegrationEvent>
{
    public WorkItemPlanningChangedIntegrationEvent Map(WorkItemPlanningChangedDomainEvent domainEvent) =>
        new(
            Guid.NewGuid().ToString("N"),
            domainEvent.WorkItemId,
            domainEvent.SprintId,
            domainEvent.EstimatePoints,
            domainEvent.OccurredAt);

    public WorkItemMovedIntegrationEvent Map(WorkItemMovedDomainEvent domainEvent) =>
        new(
            Guid.NewGuid().ToString("N"),
            domainEvent.WorkItemId,
            domainEvent.ProjectId,
            domainEvent.BoardId,
            domainEvent.FromStatus,
            domainEvent.ToStatus,
            domainEvent.OccurredAt);
}
