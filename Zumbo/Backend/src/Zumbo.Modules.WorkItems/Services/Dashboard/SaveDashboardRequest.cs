using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record SaveDashboardRequest(
    string Name,
    string? Description,
    string Scope,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<DashboardWidgetRequest> Widgets,
    DashboardFilterRequest? Filter = null);
