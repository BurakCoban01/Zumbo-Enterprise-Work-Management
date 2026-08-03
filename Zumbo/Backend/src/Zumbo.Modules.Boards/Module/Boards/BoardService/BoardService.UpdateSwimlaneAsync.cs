using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public async Task<BoardResponse> UpdateSwimlaneAsync(
        string boardId,
        UpdateSwimlaneRequest request,
        CancellationToken ct)
        => await UpdateSwimlaneAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> UpdateSwimlaneAsync(
        string boardId,
        UpdateSwimlaneRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var oldMode = board.SwimlaneMode;
        board.SwimlaneMode = NormalizeSwimlaneMode(request.Mode);
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardSwimlaneUpdated", board.Id, oldMode, board.SwimlaneMode, correlationId, ct);
        return ToResponse(board);
    }
}
