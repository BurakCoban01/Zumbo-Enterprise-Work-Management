using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record GoalSourceResult(
    IReadOnlyCollection<GoalInitiativeSource> Initiatives,
    IReadOnlyCollection<GoalProjectSource> Projects,
    IReadOnlyCollection<string> UnavailableSources);
