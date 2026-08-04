using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemApprovalResponse(
    string Id,
    string FromStatus,
    string ToStatus,
    string RequestedByUserId,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    string? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? Note,
    DateTimeOffset? ConsumedAt);
