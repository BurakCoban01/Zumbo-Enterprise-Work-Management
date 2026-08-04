using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Columns;

public sealed class AddColumnHandler(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    IClock clock,
    ICurrentUser currentUser,
    IBoardAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null,
    IBoardWorkflowCatalog? workflowCatalog = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<BoardResponse> HandleAsync(
        string boardId,
        CreateColumnRequest request,
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
        var name = AddColumnValidator.NormalizeName(request.Name);
        var category = AddColumnValidator.NormalizeCategory(request.Category);
        AddColumnValidator.ValidateWipLimit(request.WipLimit);
        EnsureUnique(board, name, category);
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
        await audit.WriteAsync("BoardColumnCreated", board.Id, null, $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}", correlationId, ct);
        return BoardResponseMapper.ToResponse(board, currentUser);
    }

    private static void EnsureUnique(BoardDocument board, string name, string category)
    {
        if (board.Columns.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_COLUMN_NAME_EXISTS", "Column name must be unique inside the board.");
        }

        if (category != "Custom" && board.Columns.Any(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_COLUMN_CATEGORY_EXISTS", "A board can contain only one standard column per category.");
        }
    }
}
