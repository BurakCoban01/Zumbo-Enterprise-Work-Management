using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    private static int Progress(GoalDocument goal)
    {
        if (goal.KeyResults.Count == 0) return 0;
        return (int)Math.Round(goal.KeyResults.Average(Progress));
    }

    private static int Progress(KeyResultDocument keyResult)
    {
        var distance = keyResult.Direction == KeyResultDirections.Increase
            ? keyResult.TargetValue - keyResult.BaselineValue
            : keyResult.BaselineValue - keyResult.TargetValue;
        var travelled = keyResult.Direction == KeyResultDirections.Increase
            ? keyResult.CurrentValue - keyResult.BaselineValue
            : keyResult.BaselineValue - keyResult.CurrentValue;
        if (distance <= 0) return 0;
        return Math.Clamp((int)Math.Round(travelled * 100m / distance), 0, 100);
    }
}
