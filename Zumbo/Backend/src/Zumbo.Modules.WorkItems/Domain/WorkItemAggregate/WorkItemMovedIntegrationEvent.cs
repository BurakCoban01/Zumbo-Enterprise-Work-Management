using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemMovedIntegrationEvent(
    string EventId,
    string AggregateId,
    string ProjectId,
    string BoardId,
    string FromStatus,
    string ToStatus,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public string EventName => "work-item.moved.v1";
}
