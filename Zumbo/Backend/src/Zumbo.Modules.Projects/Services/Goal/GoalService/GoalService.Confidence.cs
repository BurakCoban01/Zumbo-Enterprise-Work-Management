using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    private static int? Confidence(int? value, string label)
    {
        if (value is < 0 or > 100)
            throw new ValidationException($"{label} must be between 0 and 100.");
        return value;
    }
}
