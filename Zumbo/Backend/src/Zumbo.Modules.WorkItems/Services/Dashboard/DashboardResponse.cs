using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record DashboardResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string? Description,
    string Scope,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<DashboardWidgetResponse> Widgets,
    DashboardFilterRequest Filter,
    IReadOnlyCollection<string> ViewerUserIds,
    bool CanEdit,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version) : IVersionedResource;
