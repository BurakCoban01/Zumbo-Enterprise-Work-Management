using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class WorkflowDomainEventMapper :
    IIntegrationEventMapper<WorkflowDefinedDomainEvent, WorkflowDefinedIntegrationEvent>
{
    public WorkflowDefinedIntegrationEvent Map(WorkflowDefinedDomainEvent domainEvent) =>
        new(
            Guid.NewGuid().ToString("N"),
            domainEvent.WorkflowId,
            domainEvent.ProjectId,
            domainEvent.StatusCount,
            domainEvent.TransitionCount,
            domainEvent.OccurredAt);
}
