using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    private static string Allowed(string? value, IReadOnlySet<string> allowed, string label)
    {
        var normalized = Required(value, label, 32);
        return allowed.Contains(normalized)
            ? normalized
            : throw new ValidationException($"{label} is not supported.");
    }
}
