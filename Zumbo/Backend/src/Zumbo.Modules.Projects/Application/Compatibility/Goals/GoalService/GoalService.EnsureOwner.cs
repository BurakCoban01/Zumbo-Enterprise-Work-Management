using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    private static void EnsureOwner(GoalDocument goal, string userId)
    {
        EnsureVisible(goal, userId);
        if (goal.OwnerUserId != userId)
            throw new ForbiddenException("Only the goal owner can change this goal.");
    }
}
