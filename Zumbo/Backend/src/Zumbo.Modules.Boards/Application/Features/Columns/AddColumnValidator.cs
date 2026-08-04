using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Columns;

public static class AddColumnValidator
{
    public static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 80)
        {
            throw new ValidationException("Board column name must contain 1-80 characters.");
        }

        return normalized;
    }

    public static string NormalizeCategory(string category)
    {
        var normalized = string.IsNullOrWhiteSpace(category) ? "Custom" : category.Trim();
        var known = new[] { "Todo", "InProgress", "Review", "Test", "Done", "Custom" };
        return known.SingleOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? normalized;
    }

    public static void ValidateWipLimit(int? wipLimit)
    {
        if (wipLimit is < 1 or > 1000)
        {
            throw new ValidationException("WIP limit must be between 1 and 1000 when provided.");
        }
    }

}
