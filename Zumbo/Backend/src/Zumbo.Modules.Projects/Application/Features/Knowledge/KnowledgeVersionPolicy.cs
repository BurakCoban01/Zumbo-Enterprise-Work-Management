using System.Text.RegularExpressions;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal static partial class KnowledgeVersionPolicy
{
    internal static KnowledgeVersionDocument Normalize(
        string? title,
        string? contentMarkdown,
        IReadOnlyCollection<string>? tags,
        IReadOnlyCollection<string>? workItemIds,
        IReadOnlyCollection<string>? userIds,
        string? changeSummary,
        string authorUserId,
        int number,
        DateTimeOffset createdAt) => new()
    {
        Number = number,
        Title = KnowledgeQueryInput.Required(title, "Knowledge document title", 160),
        ContentMarkdown = NormalizeContent(contentMarkdown),
        Tags = NormalizeLabels(tags, KnowledgeLimits.MaximumTags),
        WorkItemIds = NormalizeIds(
            workItemIds,
            KnowledgeLimits.MaximumWorkItemLinks,
            "Knowledge work-item link"),
        UserIds = NormalizeIds(
            userIds,
            KnowledgeLimits.MaximumUserLinks,
            "Knowledge user link"),
        ChangeSummary = KnowledgeQueryInput.Required(
            changeSummary,
            "Knowledge version summary",
            500),
        AuthorUserId = authorUserId,
        CreatedAt = createdAt
    };

    internal static void Apply(
        KnowledgeDocument document,
        KnowledgeVersionDocument version)
    {
        document.Title = version.Title;
        document.ContentMarkdown = version.ContentMarkdown;
        document.Tags = [.. version.Tags];
        document.WorkItemIds = [.. version.WorkItemIds];
        document.UserIds = [.. version.UserIds];
        document.CurrentContentVersion = version.Number;
    }

    internal static void EnsureCanEdit(
        KnowledgeDocument document,
        KnowledgeScopeAccess access,
        string userId)
    {
        if (document.OwnerUserId != userId && !access.CanManage)
            throw new ForbiddenException("Knowledge document edit access is required.");
    }

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

    [GeneratedRegex(@"<\s*/?\s*[A-Za-z]", RegexOptions.CultureInvariant)]
    private static partial Regex RawHtmlPattern();

    [GeneratedRegex(
        @"!?\[[^\]\r\n]{0,500}\]\(\s*(?<target>[^\s\)]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkPattern();
}
