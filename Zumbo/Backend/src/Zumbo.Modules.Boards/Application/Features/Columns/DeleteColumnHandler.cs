using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Columns;

public sealed class DeleteColumnHandler(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    IBoardColumnUsageChecker usageChecker,
    IClock clock,
    ICurrentUser currentUser,
    IBoardAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<BoardResponse> HandleAsync(DeleteColumnCommand command, CancellationToken ct)
    {
        var board = await boards.SelectAsync(x => x.Id == command.BoardId && !x.Archived, ct)
            ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        await accessChecker.EnsureCanAsync(userId, board.ProjectId, "BoardManage", ct);
        var column = board.Columns.SingleOrDefault(x => x.Id == command.ColumnId)
            ?? throw new NotFoundException("BOARD_COLUMN_NOT_FOUND", "Board column was not found.");

        if (column.Category == "Done")
        {
            throw new ConflictException("DONE_COLUMN_LOCKED", "Done column cannot be removed without a migration.");
        }

        if (column.Category == "Todo")
        {
            throw new ConflictException("TODO_COLUMN_LOCKED", "To Do column cannot be removed without a workflow migration.");
        }

        if (board.Columns.Count <= 1)
        {
            throw new ConflictException("BOARD_REQUIRES_COLUMN", "A board must contain at least one column.");
        }

        if (await usageChecker.HasWorkItemsAsync(board.Id, column.Id, column.Name, ct))
        {
            throw new ConflictException("BOARD_COLUMN_IN_USE", "Move work items before deleting this column.");
        }

        board.Columns.Remove(column);
        var position = 1;
        foreach (var item in board.Columns.OrderBy(x => x.Position))
        {
            item.Position = position++;
        }

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
        await audit.WriteAsync(
            "BoardColumnDeleted",
            board.Id,
            $"{column.Id}:{column.Name}:{column.Category}:{column.WipLimit}",
            null,
            command.CorrelationId,
            ct);
        return BoardResponseMapper.ToResponse(board, currentUser);
    }
}
