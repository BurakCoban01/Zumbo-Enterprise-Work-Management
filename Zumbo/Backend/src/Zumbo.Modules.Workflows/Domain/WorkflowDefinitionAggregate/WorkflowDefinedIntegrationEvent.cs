using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record WorkflowDefinedIntegrationEvent(
    string EventId,
    string AggregateId,
    string ProjectId,
    int StatusCount,
    int TransitionCount,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public string EventName => "workflow.defined.v1";
}
