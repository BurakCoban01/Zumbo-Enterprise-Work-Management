using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal static class WorkItemCommentRules
{
    internal static string NormalizeBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ValidationException("Comment body is required.");
        }

        var normalized = body.Trim();
        if (normalized.Length > 10_000)
        {
            throw new ValidationException("Comment body cannot exceed 10000 characters.");
        }

        return normalized;
    }

    internal static List<string> NormalizeMentions(IReadOnlyCollection<string>? mentions)
    {
        var normalized = mentions?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
        if (normalized.Count > 50)
        {
            throw new ValidationException("A comment cannot mention more than 50 users.");
        }

        if (normalized.Any(x => x.Length > 128))
        {
            throw new ValidationException("Mentioned user ids cannot exceed 128 characters.");
        }

        return normalized;
    }

    internal static string? NormalizeIdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > 128 || normalized.Any(char.IsControl))
        {
            throw new ValidationException(
                "Comment idempotency key cannot exceed 128 characters or contain control characters.");
        }

        return normalized;
    }
}
