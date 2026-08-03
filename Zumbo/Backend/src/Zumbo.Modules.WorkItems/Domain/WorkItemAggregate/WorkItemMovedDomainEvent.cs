using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemMovedDomainEvent(
    string WorkItemId,
    string ProjectId,
    string BoardId,
    string FromStatus,
    string ToStatus,
    DateTimeOffset OccurredAt) : IDomainEvent;
