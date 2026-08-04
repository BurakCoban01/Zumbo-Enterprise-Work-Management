using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record SaveGoalRequest(
    string Name,
    string? Description,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyCollection<string> ViewerUserIds,
    IReadOnlyCollection<GoalInitiativeLinkRequest> InitiativeLinks,
    IReadOnlyCollection<string> ProjectIds);
