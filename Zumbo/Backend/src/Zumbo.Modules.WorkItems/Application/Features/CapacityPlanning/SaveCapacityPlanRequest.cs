using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record SaveCapacityPlanRequest(
    string Name,
    string? Description,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? PortfolioId,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<CapacityMemberRequest> Members,
    IReadOnlyCollection<CapacityAllocationRequest> Allocations,
    IReadOnlyCollection<string> ViewerUserIds);
