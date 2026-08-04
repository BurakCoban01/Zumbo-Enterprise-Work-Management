using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityPlanPageResponse(
    IReadOnlyCollection<CapacityPlanResponse> Items,
    int Page,
    int PageSize,
    long Total);
