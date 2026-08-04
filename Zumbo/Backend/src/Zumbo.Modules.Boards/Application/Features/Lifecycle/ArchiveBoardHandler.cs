using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Lifecycle;

public sealed class ArchiveBoardHandler(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    IBoardColumnUsageChecker usageChecker,
    IClock clock,
    ICurrentUser currentUser,
    IBoardAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task HandleAsync(ArchiveBoardCommand command, CancellationToken ct)
    {
        ArchiveBoardValidator.Validate(command);
        var board = await boards.SelectAsync(x => x.Id == command.BoardId && !x.Archived, ct)
            ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");

        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        await accessChecker.EnsureCanAsync(userId, board.ProjectId, "BoardManage", ct);
        if (await usageChecker.HasBoardWorkItemsAsync(board.Id, ct))
        {
            throw new ConflictException("BOARD_IN_USE", "Archive or move active work items before archiving the board.");
        }

        board.Archived = true;
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
        await audit.WriteAsync("BoardArchived", board.Id, "active", "archived", command.CorrelationId, ct);
    }
}
