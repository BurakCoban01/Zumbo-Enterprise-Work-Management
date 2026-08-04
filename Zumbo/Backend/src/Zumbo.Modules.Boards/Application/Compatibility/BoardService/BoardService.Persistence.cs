using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService
{
    private async Task<IAsyncDisposable> AcquireLockAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("BOARD_RESOURCE_BUSY", "Board resource is busy; retry the operation.");
    }

    private async Task<BoardDocument> GetArchivedBoard(string boardId, CancellationToken ct) =>
        await boards.SelectAsync(x => x.Id == boardId && x.Archived, ct)
        ?? throw new NotFoundException("BOARD_NOT_FOUND", "Archived board was not found.");

    private async Task<BoardDocument> GetBoard(string boardId, CancellationToken ct) =>
        await boards.SelectAsync(x => x.Id == boardId && !x.Archived, ct)
        ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");

    private async Task SaveAsync(BoardDocument board, CancellationToken ct)
    {
        board.UpdatedAt = clock.UtcNow;
        var result = await boards.ReplaceByVersionAsync(
            x => x.Id == board.Id,
            board,
            expectedVersion.Consume(board.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        }

        board.Version = result.Version!.Value;
    }

    private BoardResponse ToResponse(BoardDocument board) =>
        new(
            board.Id,
            board.ProjectId,
            board.Name,
            board.Type,
            board.SwimlaneMode,
            board.Columns.OrderBy(x => x.Position).Select(x =>
                new BoardColumnResponse(x.Id, x.Name, x.Category, x.Position, x.WipLimit, BoardWorkflowMappingRules.EnsureStatusNames(board, x))).ToList(),
            board.Views
                .Where(x => x.IsShared || x.OwnerUserId == CurrentUserId())
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
