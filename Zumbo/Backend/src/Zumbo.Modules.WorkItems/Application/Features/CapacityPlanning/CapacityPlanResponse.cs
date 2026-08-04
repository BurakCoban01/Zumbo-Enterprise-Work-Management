using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityPlanResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string? Description,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? PortfolioId,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<CapacityMemberResponse> Members,
    IReadOnlyCollection<CapacityAllocationResponse> Allocations,
    IReadOnlyCollection<string> ViewerUserIds,
    bool CanEdit,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version) : IVersionedResource;
