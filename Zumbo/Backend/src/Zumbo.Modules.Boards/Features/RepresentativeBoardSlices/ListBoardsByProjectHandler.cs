using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed class ListBoardsByProjectHandler(BoardService service)
{
    public Task<IReadOnlyList<BoardResponse>> HandleAsync(ListBoardsByProjectQuery query, CancellationToken ct)
    {
        ListBoardsByProjectValidator.Validate(query);
        return service.ListByProjectAsync(query.ProjectId, ct, query.Archived);
    }
}
