namespace Zumbo.Modules.Boards;

public sealed partial class BoardService
{
    public async Task<BoardResponse> CreateViewAsync(
        string boardId,
        CreateBoardViewRequest request,
        CancellationToken ct)
        => await CreateViewAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> CreateViewAsync(
        string boardId,
        CreateBoardViewRequest request,
        string correlationId,
        CancellationToken ct) =>
        await createViewHandler.HandleAsync(boardId, request, correlationId, ct);

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
        CancellationToken ct) =>
        await updateViewHandler.HandleAsync(boardId, viewId, request, correlationId, ct);

    public async Task<BoardResponse> DeleteViewAsync(
        string boardId,
        string viewId,
        CancellationToken ct)
        => await DeleteViewAsync(boardId, viewId, "none", ct);

    public async Task<BoardResponse> DeleteViewAsync(
        string boardId,
        string viewId,
        string correlationId,
        CancellationToken ct) =>
        await deleteViewHandler.HandleAsync(boardId, viewId, correlationId, ct);
}
