using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record GoalInitiativeSource(
    string PortfolioId,
    string Id,
    string Name,
    string Status,
    string Health,
    int? Confidence);
