using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public Task<BoardResponse> AddColumnAsync(string boardId, CreateColumnRequest request, CancellationToken ct) =>
        AddColumnAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> AddColumnAsync(string boardId, CreateColumnRequest request, string correlationId, CancellationToken ct)
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var name = NormalizeColumnName(request.Name);
        var category = NormalizeCategory(request.Category);
        ValidateWipLimit(request.WipLimit);
        EnsureUniqueColumn(board, name, category);
        var statusNames = BoardWorkflowMappingRules.NormalizeStatusNames(request.StatusNames, name);
        await BoardWorkflowMappingRules.EnsureAvailableAsync(workflowCatalog, board, statusNames, null, ct);
        var nextPosition = board.Columns.Count == 0 ? 1 : board.Columns.Max(x => x.Position) + 1;
        var column = new BoardColumnDocument
        {
            Name = name,
            Category = category,
            WipLimit = request.WipLimit,
            StatusNames = statusNames,
            Position = nextPosition
        };
        board.Columns.Add(column);

        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardColumnCreated", board.Id, null, $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}", correlationId, ct);
        return ToResponse(board);
    }
}
