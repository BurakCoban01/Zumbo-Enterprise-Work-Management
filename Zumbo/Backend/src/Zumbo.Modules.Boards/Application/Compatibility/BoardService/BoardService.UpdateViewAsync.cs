using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public async Task<BoardResponse> UpdateViewAsync(
        string boardId,
        string viewId,
        UpdateBoardViewRequest request,
        CancellationToken ct)
        => await UpdateViewAsync(boardId, viewId, request, "none", ct);

    public async Task<BoardResponse> UpdateViewAsync(
        string boardId,
        string viewId,
        UpdateBoardViewRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        var view = board.Views.SingleOrDefault(x => x.Id == viewId)
            ?? throw new NotFoundException("BOARD_VIEW_NOT_FOUND", "Board view was not found.");
        await EnsureCanMutateViewAsync(board, view, request.IsShared, ct);
        var name = NormalizeViewName(request.Name);
        EnsureUniqueViewName(board, name, view.OwnerUserId, request.IsShared, view.Id);
        var oldValue = $"{view.Id}:{view.Name}:{view.IsShared}:{view.SwimlaneMode}";
        view.Name = name;
        view.IsShared = request.IsShared;
        view.SwimlaneMode = NormalizeSwimlaneMode(request.SwimlaneMode);
        view.Filter = NormalizeFilter(request.Filter);
        view.UpdatedAt = clock.UtcNow;
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardViewUpdated", board.Id, oldValue, $"{view.Id}:{view.Name}:{view.IsShared}:{view.SwimlaneMode}", correlationId, ct);
        return ToResponse(board);
    }
}
