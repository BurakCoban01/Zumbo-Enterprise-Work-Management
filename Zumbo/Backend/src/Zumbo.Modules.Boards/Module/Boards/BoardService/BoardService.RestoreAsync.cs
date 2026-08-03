using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public Task<BoardResponse> RestoreAsync(string boardId, CancellationToken ct) =>
        RestoreAsync(boardId, "none", ct);

    public async Task<BoardResponse> RestoreAsync(string boardId, string correlationId, CancellationToken ct)
    {
        var board = await GetArchivedBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var duplicate = await boards.SelectAsync(x =>
            x.Id != board.Id
            && x.ProjectId == board.ProjectId
            && !x.Archived
            && x.Name.ToLower() == board.Name.ToLower(), ct);
        if (duplicate is not null)
        {
            throw new ConflictException("BOARD_NAME_EXISTS", "An active board already uses this name.");
        }

        board.Archived = false;
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardRestored", board.Id, "archived", "active", correlationId, ct);
        return ToResponse(board);
    }
}
