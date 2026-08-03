using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemAuditEvent(
    string ActorUserId,
    string Action,
    string EntityType,
    string EntityId,
    string? OldValue,
    string? NewValue,
    string? IpAddress,
    string? UserAgent,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string DeduplicationKey);
