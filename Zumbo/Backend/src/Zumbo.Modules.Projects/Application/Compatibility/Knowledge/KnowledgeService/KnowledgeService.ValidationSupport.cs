using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService
{
    private static string AllowedScope(string? value)
    {
        var normalized = Required(value, "Knowledge scope type", 32);
        return KnowledgeScopeTypes.Allowed.Contains(normalized)
            ? normalized
            : throw new ValidationException("Knowledge scope type is not supported.");
    }

    [GeneratedRegex(
        @"!?\[[^\]\r\n]{0,500}\]\(\s*(?<target>[^\s\)]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkPattern();

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

    private static List<string> NormalizeIds(
        IReadOnlyCollection<string>? values,
        int maximum,
        string label)
    {
        var normalized = (values
                ?? throw new ValidationException($"{label} list is required."))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalized.Count > maximum
            || normalized.Any(value => value.Length > 128))
        {
            throw new ValidationException($"{label} list is outside the supported bounds.");
        }
        return normalized;
    }

    private static List<string> NormalizeLabels(
        IReadOnlyCollection<string>? values,
        int maximum)
    {
        var normalized = (values
                ?? throw new ValidationException("Knowledge tag list is required."))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count > maximum
            || normalized.Any(value => value.Length > 32))
        {
            throw new ValidationException("Knowledge tag list is outside the supported bounds.");
        }
        return normalized;
    }

    private static KnowledgeVersionDocument NormalizeVersion(
        string? title,
        string? contentMarkdown,
        IReadOnlyCollection<string>? tags,
        IReadOnlyCollection<string>? workItemIds,
        IReadOnlyCollection<string>? userIds,
        string? changeSummary,
        string authorUserId,
        int number,
        DateTimeOffset createdAt)
    {
        var content = NormalizeContent(contentMarkdown);
        return new KnowledgeVersionDocument
        {
            Number = number,
            Title = Required(title, "Knowledge document title", 160),
            ContentMarkdown = content,
            Tags = NormalizeLabels(tags, KnowledgeLimits.MaximumTags),
            WorkItemIds = NormalizeIds(
                workItemIds,
                KnowledgeLimits.MaximumWorkItemLinks,
                "Knowledge work-item link"),
            UserIds = NormalizeIds(
                userIds,
                KnowledgeLimits.MaximumUserLinks,
                "Knowledge user link"),
            ChangeSummary = Required(changeSummary, "Knowledge version summary", 500),
            AuthorUserId = authorUserId,
            CreatedAt = createdAt
        };
    }

    private static string? Optional(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"Value cannot exceed {maximum} characters.");
        return normalized;
    }

    [GeneratedRegex(@"<\s*/?\s*[A-Za-z]", RegexOptions.CultureInvariant)]
    private static partial Regex RawHtmlPattern();

    private static string Required(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{label} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        return normalized;
    }

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
