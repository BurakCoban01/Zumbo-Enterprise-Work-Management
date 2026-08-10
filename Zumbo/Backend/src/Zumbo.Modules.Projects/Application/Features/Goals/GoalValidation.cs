using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal static class GoalValidation
{
    internal static string Allowed(
        string? value,
        IReadOnlySet<string> allowed,
        string label)
    {
        var normalized = Required(value, label, 32);
        return allowed.Contains(normalized)
            ? normalized
            : throw new ValidationException($"{label} is not supported.");
    }

    internal static int? Confidence(int? value, string label)
    {
        if (value is < 0 or > 100)
            throw new ValidationException($"{label} must be between 0 and 100.");
        return value;
    }

    internal static void EnsureFinite(decimal value, string label)
    {
        const decimal maximumMagnitude = 1_000_000_000_000m;
        if (value is < -maximumMagnitude or > maximumMagnitude)
            throw new ValidationException($"{label} is outside the supported range.");
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
