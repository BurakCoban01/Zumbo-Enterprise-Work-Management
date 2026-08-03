using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemSearchPageResponse(
    IReadOnlyList<WorkItemResponse> Items,
    long TotalCount,
    bool Degraded);
