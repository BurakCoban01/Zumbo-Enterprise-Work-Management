using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record PortfolioProjectMilestoneSource(
    string Id,
    string Name,
    DateTimeOffset DueAt,
    string Status,
    DateTimeOffset? CompletedAt);
