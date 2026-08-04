using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Lifecycle;

public sealed class RestoreBoardHandler(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    IClock clock,
    ICurrentUser currentUser,
    IBoardAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<BoardResponse> HandleAsync(RestoreBoardCommand command, CancellationToken ct)
    {
        RestoreBoardValidator.Validate(command);
        var board = await boards.SelectAsync(x => x.Id == command.BoardId && x.Archived, ct)
            ?? throw new NotFoundException("BOARD_NOT_FOUND", "Archived board was not found.");

        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        await accessChecker.EnsureCanAsync(userId, board.ProjectId, "BoardManage", ct);
        var duplicate = await boards.SelectAsync(x =>
            x.Id != board.Id
            && x.ProjectId == board.ProjectId
            && !x.Archived
            && x.Name.ToLower() == board.Name.ToLower(), ct);
        if (duplicate is not null)
        {
            throw new ConflictException("BOARD_NAME_EXISTS", "An active board already uses this name.");
        }

        board.Archived = false;
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
        await audit.WriteAsync("BoardRestored", board.Id, "archived", "active", command.CorrelationId, ct);
        return BoardResponseMapper.ToResponse(board, currentUser);
    }
}
