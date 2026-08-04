using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService
{
    private string CurrentUserId() =>
        !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : throw new UnauthorizedException("Authenticated user is required.");

    private async Task EnsureCanMutateViewAsync(
        BoardDocument board,
        BoardViewDocument view,
        bool targetIsShared,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (!view.IsShared && view.OwnerUserId != userId)
        {
            throw new NotFoundException("BOARD_VIEW_NOT_FOUND", "Board view was not found.");
        }

        await EnsurePermissionAsync(
            board.ProjectId,
            view.IsShared || targetIsShared ? "BoardManage" : "BoardView",
            ct);
    }

    private async Task EnsurePermissionAsync(string projectId, string permission, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        await accessChecker.EnsureCanAsync(userId, projectId, permission, ct);
    }

    private static void EnsureUniqueColumn(
        BoardDocument board,
        string name,
        string category,
        string? ignoredColumnId = null)
    {
        if (board.Columns.Any(x => x.Id != ignoredColumnId && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_COLUMN_NAME_EXISTS", "Column name must be unique inside the board.");
        }

        if (category != "Custom" && board.Columns.Any(x =>
            x.Id != ignoredColumnId && x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_COLUMN_CATEGORY_EXISTS", "A board can contain only one standard column per category.");
        }
    }

    private static void EnsureUniqueViewName(
        BoardDocument board,
        string name,
        string ownerUserId,
        bool isShared,
        string? ignoredViewId = null)
    {
        if (board.Views.Any(x =>
            x.Id != ignoredViewId
            && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && (isShared || x.IsShared || x.OwnerUserId == ownerUserId)))
        {
            throw new ConflictException("BOARD_VIEW_NAME_EXISTS", "Board view name must be unique in its visibility scope.");
        }
    }

    private static string NormalizeCategory(string category)
    {
        var normalized = string.IsNullOrWhiteSpace(category) ? "Custom" : category.Trim();
        var known = new[] { "Todo", "InProgress", "Review", "Test", "Done", "Custom" };
        return known.SingleOrDefault(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? normalized;
    }

    private static string NormalizeColumnName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 80)
        {
            throw new ValidationException("Board column name must contain 1-80 characters.");
        }

        return normalized;
    }

    private static BoardFilterDocument NormalizeFilter(BoardFilterRequest? filter)
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

        return new BoardFilterDocument
        {
            AssigneeUserId = NormalizeOptionalId(filter.AssigneeUserId),
            TeamId = NormalizeOptionalId(filter.TeamId),
            Statuses = statuses,
            Priorities = priorities,
            Labels = labels,
            Text = text
        };
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

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 100)
        {
            throw new ValidationException("Board name must contain 2-100 characters.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeSwimlaneMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        null or "" or "none" => "None",
        "assignee" => "Assignee",
        "priority" => "Priority",
        "team" => "Team",
        "epic" => "Epic",
        _ => throw new ValidationException("Swimlane mode must be None, Assignee, Priority, Team or Epic.")
    };

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type) || string.Equals(type, "Kanban", StringComparison.OrdinalIgnoreCase))
        {
            return "Kanban";
        }

        if (string.Equals(type, "Scrum", StringComparison.OrdinalIgnoreCase))
        {
            return "Scrum";
        }

        throw new ValidationException("Board type must be Kanban or Scrum.");
    }

    private static string NormalizeViewName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 2 or > 80)
        {
            throw new ValidationException("Board view name must contain 2-80 characters.");
        }

        return normalized;
    }

    private static void ValidateWipLimit(int? wipLimit)
    {
        if (wipLimit is < 1 or > 1000)
        {
            throw new ValidationException("WIP limit must be between 1 and 1000 when provided.");
        }
    }
}
