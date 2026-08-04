using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private static string AllowedScope(string? value)
    {
        var normalized = Required(value, "Knowledge scope type", 32);
        return KnowledgeScopeTypes.Allowed.Contains(normalized)
            ? normalized
            : throw new ValidationException("Knowledge scope type is not supported.");
    }
}
