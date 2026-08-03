using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public Task<BoardResponse> DeleteColumnAsync(string boardId, string columnId, CancellationToken ct) =>
        DeleteColumnAsync(boardId, columnId, "none", ct);

    public async Task<BoardResponse> DeleteColumnAsync(string boardId, string columnId, string correlationId, CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var column = board.Columns.SingleOrDefault(x => x.Id == columnId)
            ?? throw new NotFoundException("BOARD_COLUMN_NOT_FOUND", "Board column was not found.");

        if (column.Category == "Done")
        {
            throw new ConflictException("DONE_COLUMN_LOCKED", "Done column cannot be removed without a migration.");
        }

        if (column.Category == "Todo")
        {
            throw new ConflictException("TODO_COLUMN_LOCKED", "To Do column cannot be removed without a workflow migration.");
        }

        if (board.Columns.Count <= 1)
        {
            throw new ConflictException("BOARD_REQUIRES_COLUMN", "A board must contain at least one column.");
        }

        if (await usageChecker.HasWorkItemsAsync(board.Id, column.Id, column.Name, ct))
        {
            throw new ConflictException("BOARD_COLUMN_IN_USE", "Move work items before deleting this column.");
        }

        board.Columns.Remove(column);
        var position = 1;
        foreach (var item in board.Columns.OrderBy(x => x.Position))
        {
            item.Position = position++;
        }

        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardColumnDeleted", board.Id, $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}", null, correlationId, ct);
        return ToResponse(board);
    }
}
