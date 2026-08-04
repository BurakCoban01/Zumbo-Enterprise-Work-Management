using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards.Application.Features.Views;

public sealed class CreateViewHandler(
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
        CreateBoardViewRequest request,
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

        await accessChecker.EnsureCanAsync(userId, board.ProjectId, request.IsShared ? "BoardManage" : "BoardView", ct);
        var name = BoardViewValidator.NormalizeName(request.Name);
        EnsureUniqueName(board, name, userId, request.IsShared);
        var filter = BoardViewValidator.NormalizeFilter(request.Filter);
        var now = clock.UtcNow;
        var view = new BoardViewDocument
        {
            Name = name,
            OwnerUserId = userId,
            IsShared = request.IsShared,
            SwimlaneMode = BoardViewValidator.NormalizeSwimlaneMode(request.SwimlaneMode),
            Filter = new BoardFilterDocument
            {
                AssigneeUserId = filter.AssigneeUserId,
                TeamId = filter.TeamId,
                Statuses = filter.Statuses.ToList(),
                Priorities = filter.Priorities.ToList(),
                Labels = filter.Labels.ToList(),
                Text = filter.Text
            },
            CreatedAt = now,
            UpdatedAt = now
        };
        board.Views.Add(view);
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
            "BoardViewCreated",
            board.Id,
            null,
            $"{view.Id}:{view.Name}:{view.IsShared}",
            correlationId,
            ct);
        return BoardResponseMapper.ToResponse(board, currentUser);
    }

    private static void EnsureUniqueName(BoardDocument board, string name, string ownerUserId, bool isShared)
    {
        if (board.Views.Any(x =>
            x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && (isShared || x.IsShared || x.OwnerUserId == ownerUserId)))
        {
            throw new ConflictException("BOARD_VIEW_NAME_EXISTS", "Board view name must be unique in its visibility scope.");
        }
    }
}
