using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record PortfolioRoadmapProjectResponse(
    string Id,
    string Key,
    string Name,
    int TotalWorkItems,
    int CompletedWorkItems,
    int OverdueWorkItems,
    int Progress,
    IReadOnlyCollection<PortfolioProjectMilestoneSource> Milestones,
    DateTimeOffset UpdatedAt);
