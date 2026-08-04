using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

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
    {
        var board = await GetBoard(boardId, ct);
        await EnsurePermissionAsync(board.ProjectId, "BoardManage", ct);
        var column = board.Columns.SingleOrDefault(x => x.Id == columnId)
            ?? throw new NotFoundException("BOARD_COLUMN_NOT_FOUND", "Board column was not found.");
        var name = NormalizeColumnName(request.Name);
        var category = NormalizeCategory(request.Category);
        ValidateWipLimit(request.WipLimit);
        EnsureUniqueColumn(board, name, category, column.Id);
        var statusNames = request.StatusNames is null
            ? BoardWorkflowMappingRules.EnsureStatusNames(board, column)
            : BoardWorkflowMappingRules.NormalizeStatusNames(request.StatusNames, name);
        await BoardWorkflowMappingRules.EnsureAvailableAsync(workflowCatalog, board, statusNames, column.Id, ct);

        var identityChanges = !column.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || !column.Category.Equals(category, StringComparison.OrdinalIgnoreCase);
        if (identityChanges && column.Category != "Custom")
        {
            throw new ConflictException(
                "BOARD_SYSTEM_COLUMN_LOCKED",
                "Standard workflow column name and category cannot be changed without a workflow migration.");
        }

        if (identityChanges && await usageChecker.HasWorkItemsAsync(board.Id, column.Id, column.Name, ct))
        {
            throw new ConflictException("BOARD_COLUMN_IN_USE", "Move work items before renaming or recategorizing this column.");
        }

        if (column.Category == "Done" && category != "Done")
        {
            throw new ConflictException("DONE_COLUMN_LOCKED", "Done column category cannot be changed without a migration.");
        }

        var oldValue = $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}";
        column.Name = name;
        column.Category = category;
        column.WipLimit = request.WipLimit;
        column.StatusNames = statusNames;
        await SaveAsync(board, ct);
        await audit.WriteAsync("BoardColumnUpdated", board.Id, oldValue, $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}", correlationId, ct);
        return ToResponse(board);
    }
}
