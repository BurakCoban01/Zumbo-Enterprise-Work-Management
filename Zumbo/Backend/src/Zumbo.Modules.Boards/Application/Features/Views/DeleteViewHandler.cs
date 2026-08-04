using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Views;

public sealed class DeleteViewHandler(
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
        string viewId,
        string correlationId,
        CancellationToken ct)
    {
        var board = await boards.SelectAsync(x => x.Id == boardId && !x.Archived, ct)
            ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        var view = board.Views.SingleOrDefault(x => x.Id == viewId)
            ?? throw new NotFoundException("BOARD_VIEW_NOT_FOUND", "Board view was not found.");
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        if (!view.IsShared && view.OwnerUserId != userId)
        {
            throw new NotFoundException("BOARD_VIEW_NOT_FOUND", "Board view was not found.");
        }

        await accessChecker.EnsureCanAsync(
            userId,
            board.ProjectId,
            view.IsShared ? "BoardManage" : "BoardView",
            ct);
        board.Views.Remove(view);
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
            "BoardViewDeleted",
            board.Id,
            $"{view.Id}:{view.Name}:{view.IsShared}",
            null,
            correlationId,
            ct);
        return BoardResponseMapper.ToResponse(board, currentUser);
    }
}
