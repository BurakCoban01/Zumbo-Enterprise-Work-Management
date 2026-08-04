using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemPlanningChangedDomainEvent(
    string WorkItemId,
    string? PreviousSprintId,
    string? SprintId,
    decimal PreviousEstimatePoints,
    decimal EstimatePoints,
    DateTimeOffset OccurredAt) : IDomainEvent;
