using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    private static void EnsureVisible(GoalDocument goal, string userId)
    {
        if (!CanView(goal, userId))
            throw new NotFoundException("GOAL_NOT_FOUND", "Goal was not found.");
    }
}
