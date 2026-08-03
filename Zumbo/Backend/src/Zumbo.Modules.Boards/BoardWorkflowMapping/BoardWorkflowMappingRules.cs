using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

internal static class BoardWorkflowMappingRules
{
    public static async Task EnsureAvailableAsync(
        IBoardWorkflowCatalog? catalog,
        BoardDocument board,
        IReadOnlyCollection<string> statusNames,
        string? ignoredColumnId,
        CancellationToken ct)
    {
        if (board.Columns.Where(x => x.Id != ignoredColumnId)
            .SelectMany(x => EnsureStatusNames(board, x))
            .Any(existing => statusNames.Contains(existing, StringComparer.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_STATUS_MAPPED_MULTIPLE_TIMES", "A workflow status can map to only one board column.");
        }

        if (catalog is not null)
        {
            await catalog.EnsureStatusesAvailableAsync(board.ProjectId, statusNames, ct);
        }
    }

    public static List<string> NormalizeStatusNames(
        IReadOnlyCollection<string>? values,
        string fallback,
        bool allowEmpty = false)
    {
        var normalized = (values ?? [fallback])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if ((!allowEmpty && normalized.Count == 0) || normalized.Count > 20 || normalized.Any(x => x.Length > 80))
        {
            throw new ValidationException("A board column must map 1-20 workflow statuses of at most 80 characters.");
        }

        return normalized;
    }

    public static List<string> EnsureStatusNames(BoardDocument board, BoardColumnDocument column) =>
        board.WorkflowMappingVersion > 0 ? column.StatusNames : [column.Name];

    public static BoardResponse ToResponse(BoardDocument board, string userId) =>
        new(
            board.Id,
            board.ProjectId,
            board.Name,
            board.Type,
            board.SwimlaneMode,
            board.Columns.OrderBy(x => x.Position).Select(x =>
                new BoardColumnResponse(x.Id, x.Name, x.Category, x.Position, x.WipLimit, EnsureStatusNames(board, x))).ToList(),
            board.Views
                .Where(x => x.IsShared || x.OwnerUserId == userId)
                .OrderByDescending(x => x.IsShared)
                .ThenBy(x => x.Name)
                .Select(x => new BoardViewResponse(
                    x.Id,
                    x.Name,
                    x.OwnerUserId,
                    x.IsShared,
                    x.SwimlaneMode,
                    new BoardFilterResponse(
                        x.Filter.AssigneeUserId,
                        x.Filter.TeamId,
                        x.Filter.Statuses,
                        x.Filter.Priorities,
                        x.Filter.Labels,
                        x.Filter.Text)))
                .ToList(),
            board.Archived,
            board.Version);
}
