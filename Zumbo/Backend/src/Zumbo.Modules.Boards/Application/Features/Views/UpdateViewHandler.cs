using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Views;

public sealed class UpdateViewHandler(
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
        UpdateBoardViewRequest request,
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
            view.IsShared || request.IsShared ? "BoardManage" : "BoardView",
            ct);
        var name = BoardViewValidator.NormalizeName(request.Name);
        EnsureUniqueName(board, name, view.OwnerUserId, request.IsShared, view.Id);
        var filter = BoardViewValidator.NormalizeFilter(request.Filter);
        var oldValue = $"{view.Id}:{view.Name}:{view.IsShared}:{view.SwimlaneMode}";
        view.Name = name;
        view.IsShared = request.IsShared;
        view.SwimlaneMode = BoardViewValidator.NormalizeSwimlaneMode(request.SwimlaneMode);
        view.Filter = new BoardFilterDocument
        {
            AssigneeUserId = filter.AssigneeUserId,
            TeamId = filter.TeamId,
            Statuses = filter.Statuses.ToList(),
            Priorities = filter.Priorities.ToList(),
            Labels = filter.Labels.ToList(),
            Text = filter.Text
        };
        view.UpdatedAt = clock.UtcNow;
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
            "BoardViewUpdated",
            board.Id,
            oldValue,
            $"{view.Id}:{view.Name}:{view.IsShared}:{view.SwimlaneMode}",
            correlationId,
            ct);
        return BoardResponseMapper.ToResponse(board, currentUser);
    }

    private static void EnsureUniqueName(
        BoardDocument board,
        string name,
        string ownerUserId,
        bool isShared,
        string ignoredViewId)
    {
        if (board.Views.Any(x =>
            x.Id != ignoredViewId
            && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && (isShared || x.IsShared || x.OwnerUserId == ownerUserId)))
        {
            throw new ConflictException("BOARD_VIEW_NAME_EXISTS", "Board view name must be unique in its visibility scope.");
        }
    }
}
