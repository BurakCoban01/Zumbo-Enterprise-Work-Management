using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record GoalRollupResponse(
    string GoalId,
    string SourceStatus,
    int Progress,
    int? Confidence,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<GoalInitiativeSource> Initiatives,
    IReadOnlyCollection<GoalProjectSource> Projects,
    IReadOnlyCollection<string> UnavailableSources);
