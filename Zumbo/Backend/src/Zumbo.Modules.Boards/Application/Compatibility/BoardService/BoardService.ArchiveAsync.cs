using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public Task ArchiveAsync(string boardId, CancellationToken ct) => ArchiveAsync(boardId, "none", ct);

    public async Task ArchiveAsync(string boardId, string correlationId, CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        if (await usageChecker.HasBoardWorkItemsAsync(board.Id, ct))
        {
            throw new ConflictException("BOARD_IN_USE", "Archive or move active work items before archiving the board.");
        }

        board.Archived = true;
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardArchived", board.Id, "active", "archived", correlationId, ct);
    }
}
