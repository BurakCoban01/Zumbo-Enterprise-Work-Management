using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    private sealed record NormalizedGoalRequest(
        string Name,
        string? Description,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        List<string> ViewerUserIds,
        List<GoalInitiativeLinkRequest> InitiativeLinks,
        List<string> ProjectIds);
}
