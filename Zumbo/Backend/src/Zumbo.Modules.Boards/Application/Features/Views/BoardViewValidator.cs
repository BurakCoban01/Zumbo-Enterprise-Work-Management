using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Views;

public static class BoardViewValidator
{
    public static string NormalizeName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 80)
        {
            throw new ValidationException("Board view name must contain 2-80 characters.");
        }

        return normalized;
    }

    public static string NormalizeSwimlaneMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        null or "" or "none" => "None",
        "assignee" => "Assignee",
        "priority" => "Priority",
        "team" => "Team",
        "epic" => "Epic",
        _ => throw new ValidationException("Swimlane mode must be None, Assignee, Priority, Team or Epic.")
    };

    public static NormalizedBoardFilter NormalizeFilter(BoardFilterRequest? filter)
    {
        if (filter is null)
        {
            throw new ValidationException("Board view filter is required.");
        }

        var statuses = NormalizeFilterValues(filter.Statuses, "status", 20);
        var priorities = NormalizeFilterValues(filter.Priorities, "priority", 10);
        var labels = NormalizeFilterValues(filter.Labels, "label", 20);
        var text = string.IsNullOrWhiteSpace(filter.Text) ? null : filter.Text.Trim();
        if (text?.Length > 200)
        {
            throw new ValidationException("Board filter text cannot exceed 200 characters.");
        }

        return new NormalizedBoardFilter(
            NormalizeOptionalId(filter.AssigneeUserId),
            NormalizeOptionalId(filter.TeamId),
            statuses,
            priorities,
            labels,
            text);
    }

    private static List<string> NormalizeFilterValues(
        IReadOnlyCollection<string>? values,
        string field,
        int maximumCount)
    {
        var normalized = (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count > maximumCount || normalized.Any(x => x.Length > 80))
        {
            throw new ValidationException($"Board filter {field} values exceed the allowed limits.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
