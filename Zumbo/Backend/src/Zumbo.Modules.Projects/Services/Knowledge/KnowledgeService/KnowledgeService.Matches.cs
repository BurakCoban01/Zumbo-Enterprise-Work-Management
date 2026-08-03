using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private static bool Matches(KnowledgeDocument document, string? query)
    {
        if (query is null) return true;
        return document.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.ContentMarkdown.Contains(query, StringComparison.OrdinalIgnoreCase)
            || document.Tags.Any(tag =>
                tag.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
