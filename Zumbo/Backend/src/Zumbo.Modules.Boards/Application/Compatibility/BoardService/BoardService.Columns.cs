using Zumbo.Modules.Boards.Application.Features.ColumnOrdering;
using Zumbo.Modules.Boards.Application.Features.Columns;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService
{
    public Task<BoardResponse> AddColumnAsync(string boardId, CreateColumnRequest request, CancellationToken ct) =>
        AddColumnAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> AddColumnAsync(string boardId, CreateColumnRequest request, string correlationId, CancellationToken ct)
        => await addColumnHandler.HandleAsync(boardId, request, correlationId, ct);

    public async Task<BoardResponse> UpdateColumnAsync(
        string boardId,
        string columnId,
        UpdateColumnRequest request,
        CancellationToken ct)
        => await UpdateColumnAsync(boardId, columnId, request, "none", ct);

    public async Task<BoardResponse> UpdateColumnAsync(
        string boardId,
        string columnId,
        UpdateColumnRequest request,
        string correlationId,
        CancellationToken ct)
        => await updateColumnHandler.HandleAsync(boardId, columnId, request, correlationId, ct);

    public Task<BoardResponse> DeleteColumnAsync(string boardId, string columnId, CancellationToken ct) =>
        DeleteColumnAsync(boardId, columnId, "none", ct);

    public async Task<BoardResponse> DeleteColumnAsync(string boardId, string columnId, string correlationId, CancellationToken ct) =>
        await deleteColumnHandler.HandleAsync(new DeleteColumnCommand(boardId, columnId, correlationId), ct);

    public Task<BoardResponse> ReorderColumnsAsync(string boardId, ReorderColumnsRequest request, CancellationToken ct) =>
        ReorderColumnsAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> ReorderColumnsAsync(string boardId, ReorderColumnsRequest request, string correlationId, CancellationToken ct) =>
        await reorderColumnsHandler.HandleAsync(boardId, request, correlationId, ct);
}
