using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record SaveInitiativeRequest(
    string Name,
    string? Summary,
    string? ParentInitiativeId,
    string OwnerUserId,
    string Status,
    string Health,
    int? Confidence,
    DateTimeOffset? TargetAt,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<PortfolioMilestoneLinkRequest> MilestoneLinks);
