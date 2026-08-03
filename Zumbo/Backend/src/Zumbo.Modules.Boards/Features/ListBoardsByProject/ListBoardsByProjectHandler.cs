using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed class ListBoardsByProjectHandler(BoardService service)
{
    private ListBoardsByProjectSlice? slice;

    public ListBoardsByProjectHandler(
        IDocumentRepository<BoardDocument> boards,
        IBoardProjectAccessChecker accessChecker,
        ICurrentUser currentUser)
        : this(null!)
    {
        slice = new ListBoardsByProjectSlice(boards, accessChecker, currentUser);
    }

    public Task<IReadOnlyList<BoardResponse>> HandleAsync(
        ListBoardsByProjectQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListByProjectAsync(query.ProjectId, ct, query.Archived);
}
