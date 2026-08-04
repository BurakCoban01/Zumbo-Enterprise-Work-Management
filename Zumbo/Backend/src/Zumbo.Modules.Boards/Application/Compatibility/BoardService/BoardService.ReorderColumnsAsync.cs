using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public Task<BoardResponse> ReorderColumnsAsync(string boardId, ReorderColumnsRequest request, CancellationToken ct) =>
        ReorderColumnsAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> ReorderColumnsAsync(string boardId, ReorderColumnsRequest request, string correlationId, CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        if (request.ColumnIds is null
            || request.ColumnIds.Count != board.Columns.Count
            || request.ColumnIds.Distinct().Count() != request.ColumnIds.Count)
        {
            throw new ValidationException("Column order must include each column exactly once.");
        }

        var oldOrder = string.Join(",", board.Columns.OrderBy(x => x.Position).Select(x => x.Id));
        for (var index = 0; index < request.ColumnIds.Count; index++)
        {
            var column = board.Columns.SingleOrDefault(x => x.Id == request.ColumnIds[index])
                ?? throw new ValidationException("Unknown column id in reorder request.");
            column.Position = index + 1;
        }

        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardColumnsReordered", board.Id, oldOrder, string.Join(",", request.ColumnIds), correlationId, ct);
        return ToResponse(board);
    }
}
