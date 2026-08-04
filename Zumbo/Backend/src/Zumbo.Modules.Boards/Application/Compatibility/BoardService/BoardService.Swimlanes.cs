namespace Zumbo.Modules.Boards;

public sealed partial class BoardService
{
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
        => await updateSwimlaneHandler.HandleAsync(boardId, request, correlationId, ct);
}
