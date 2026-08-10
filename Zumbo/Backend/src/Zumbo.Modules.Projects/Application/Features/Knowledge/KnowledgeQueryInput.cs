using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

internal static class KnowledgeQueryInput
{
    internal static string AllowedScope(string? value)
    {
        var normalized = Required(value, "Knowledge scope type", 32);
        return KnowledgeScopeTypes.Allowed.Contains(normalized)
            ? normalized
            : throw new ValidationException("Knowledge scope type is not supported.");
    }

    internal static string Required(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{label} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        return normalized;
    }

    internal static string? Optional(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"Value cannot exceed {maximum} characters.");
        return normalized;
    }
}
