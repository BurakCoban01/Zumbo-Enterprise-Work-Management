using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemStatusHistoryResponse(
    string? FromStatus,
    string ToStatus,
    string ChangedByUserId,
    DateTimeOffset ChangedAt);
