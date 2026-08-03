using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record PortfolioRoadmapInitiativeResponse(
    string Id,
    string Name,
    string? ParentInitiativeId,
    string OwnerUserId,
    string Status,
    string Health,
    int? Confidence,
    DateTimeOffset? TargetAt,
    int TotalWorkItems,
    int CompletedWorkItems,
    int OverdueWorkItems,
    int Progress,
    IReadOnlyCollection<PortfolioRoadmapProjectResponse> Projects);
