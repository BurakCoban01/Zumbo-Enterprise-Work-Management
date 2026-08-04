using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public async Task<BoardResponse> DeleteViewAsync(
        string boardId,
        string viewId,
        CancellationToken ct)
        => await DeleteViewAsync(boardId, viewId, "none", ct);

    public async Task<BoardResponse> DeleteViewAsync(
        string boardId,
        string viewId,
        string correlationId,
        CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        var view = board.Views.SingleOrDefault(x => x.Id == viewId)
            ?? throw new NotFoundException("BOARD_VIEW_NOT_FOUND", "Board view was not found.");
        await EnsureCanMutateViewAsync(board, view, view.IsShared, ct);
        board.Views.Remove(view);
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardViewDeleted", board.Id, $"{view.Id}:{view.Name}:{view.IsShared}", null, correlationId, ct);
        return ToResponse(board);
    }
}
