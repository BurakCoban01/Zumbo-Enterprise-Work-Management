using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemPlanningChangedIntegrationEvent(
    string EventId,
    string AggregateId,
    string? SprintId,
    decimal EstimatePoints,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public string EventName => "work-item.planning-changed.v1";
}
