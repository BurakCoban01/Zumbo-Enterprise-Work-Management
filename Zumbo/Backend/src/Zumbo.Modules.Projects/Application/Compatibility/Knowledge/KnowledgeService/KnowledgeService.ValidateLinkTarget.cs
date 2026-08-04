using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private static void ValidateLinkTarget(string target)
    {
        var normalized = target.Trim().Trim('<', '>');
        if (normalized.StartsWith('/') || normalized.StartsWith('#'))
            return;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ValidationException(
                "Knowledge links must use HTTPS, HTTP, an internal path or an anchor.");
        }
    }
}
