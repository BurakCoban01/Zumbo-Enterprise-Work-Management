using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private static string NormalizeContent(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (normalized.Length > KnowledgeLimits.MaximumContentCharacters)
        {
            throw new ValidationException(
                $"Knowledge content cannot exceed {KnowledgeLimits.MaximumContentCharacters} characters.");
        }
        if (normalized.Any(character =>
                char.IsControl(character) && character is not ('\n' or '\t')))
        {
            throw new ValidationException("Knowledge content contains unsupported control characters.");
        }
        if (RawHtmlPattern().IsMatch(normalized))
            throw new ValidationException("Raw HTML is not supported in knowledge content.");
        foreach (Match match in MarkdownLinkPattern().Matches(normalized))
            ValidateLinkTarget(match.Groups["target"].Value);
        return normalized;
    }
}
