using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    private static void EnsureFinite(decimal value, string label)
    {
        const decimal maximumMagnitude = 1_000_000_000_000m;
        if (value is < -maximumMagnitude or > maximumMagnitude)
            throw new ValidationException($"{label} is outside the supported range.");
    }
}
