using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    private static bool CanView(GoalDocument goal, string userId) =>
        goal.OwnerUserId == userId
        || goal.ViewerUserIds.Contains(userId, StringComparer.Ordinal)
        || goal.KeyResults.Any(item => item.OwnerUserId == userId);
}
