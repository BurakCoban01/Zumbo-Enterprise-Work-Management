using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.BoardsCore;

public sealed class UpdateBoardHandler(
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
        UpdateBoardRequest request,
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
        var name = UpdateBoardValidator.NormalizeName(request.Name);
        var duplicate = await boards.SelectAsync(x =>
            x.Id != board.Id
            && x.ProjectId == board.ProjectId
            && !x.Archived
            && x.Name.ToLower() == name.ToLower(), ct);
        if (duplicate is not null)
        {
            throw new ConflictException("BOARD_NAME_EXISTS", "Board name must be unique inside the project.");
        }

        var oldValue = $"{board.Name}:{board.Type}";
        board.Name = name;
        board.Type = UpdateBoardValidator.NormalizeType(request.Type);
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
        await audit.WriteAsync("BoardUpdated", board.Id, oldValue, $"{board.Name}:{board.Type}", correlationId, ct);
        return BoardResponseMapper.ToResponse(board, currentUser);
    }
}
