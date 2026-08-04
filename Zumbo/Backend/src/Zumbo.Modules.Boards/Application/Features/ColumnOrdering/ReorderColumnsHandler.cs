using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.ColumnOrdering;

public sealed class ReorderColumnsHandler(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    IClock clock,
    ICurrentUser currentUser,
    IBoardAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<BoardResponse> HandleAsync(
        string boardId,
        ReorderColumnsRequest request,
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
        ReorderColumnsValidator.Validate(request);
        if (request.ColumnIds.Count != board.Columns.Count)
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
            "BoardColumnsReordered",
            board.Id,
            oldOrder,
            string.Join(",", request.ColumnIds),
            correlationId,
            ct);
        return BoardResponseMapper.ToResponse(board, currentUser);
    }
}
