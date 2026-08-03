using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public async Task<BoardResponse> CreateViewAsync(
        string boardId,
        CreateBoardViewRequest request,
        CancellationToken ct)
        => await CreateViewAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> CreateViewAsync(
        string boardId,
        CreateBoardViewRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, request.IsShared ? "BoardManage" : "BoardView", ct);
        var userId = CurrentUserId();
        var name = NormalizeViewName(request.Name);
        EnsureUniqueViewName(board, name, userId, request.IsShared);
        var now = clock.UtcNow;
        var view = new BoardViewDocument
        {
            Name = name,
            OwnerUserId = userId,
            IsShared = request.IsShared,
            SwimlaneMode = NormalizeSwimlaneMode(request.SwimlaneMode),
            Filter = NormalizeFilter(request.Filter),
            CreatedAt = now,
            UpdatedAt = now
        };
        board.Views.Add(view);
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardViewCreated", board.Id, null, $"{view.Id}:{view.Name}:{view.IsShared}", correlationId, ct);
        return ToResponse(board);
    }
}
