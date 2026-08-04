using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Columns;

public sealed class UpdateColumnHandler(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    IBoardColumnUsageChecker usageChecker,
    IClock clock,
    ICurrentUser currentUser,
    IBoardAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null,
    IBoardWorkflowCatalog? workflowCatalog = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<BoardResponse> HandleAsync(
        string boardId,
        string columnId,
        UpdateColumnRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var board = await boards.SelectAsync(x => x.Id == boardId && !x.Archived, ct)
            ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        await accessChecker.EnsureCanAsync(userId, board.ProjectId, "BoardManage", ct);
        var column = board.Columns.SingleOrDefault(x => x.Id == columnId)
            ?? throw new NotFoundException("BOARD_COLUMN_NOT_FOUND", "Board column was not found.");
        var name = UpdateColumnValidator.NormalizeName(request.Name);
        var category = UpdateColumnValidator.NormalizeCategory(request.Category);
        UpdateColumnValidator.ValidateWipLimit(request.WipLimit);
        EnsureUnique(board, name, category, column.Id);
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
        board.UpdatedAt = clock.UtcNow;
        var result = await boards.ReplaceByVersionAsync(
            x => x.Id == board.Id,
            board,
            expectedVersion.Consume(board.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        }

        board.Version = result.Version!.Value;
        await audit.WriteAsync("BoardColumnUpdated", board.Id, oldValue, $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}", correlationId, ct);
        return BoardResponseMapper.ToResponse(board, currentUser);
    }

    private static void EnsureUnique(BoardDocument board, string name, string category, string columnId)
    {
        if (board.Columns.Any(x => x.Id != columnId && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_COLUMN_NAME_EXISTS", "Column name must be unique inside the board.");
        }

        if (category != "Custom" && board.Columns.Any(x =>
            x.Id != columnId && x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_COLUMN_CATEGORY_EXISTS", "A board can contain only one standard column per category.");
        }
    }
}
