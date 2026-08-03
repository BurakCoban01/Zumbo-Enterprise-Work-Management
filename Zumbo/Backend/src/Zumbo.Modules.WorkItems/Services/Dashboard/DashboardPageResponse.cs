using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record DashboardPageResponse(
    IReadOnlyCollection<DashboardResponse> Items,
    int Page,
    int PageSize,
    long Total);
