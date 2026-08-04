using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public async Task<IReadOnlyList<BoardResponse>> ListByProjectAsync(
        string projectId,
        CancellationToken ct,
        bool archived = false) =>
        await listBoardsByProjectHandler.HandleAsync(
            new ListBoardsByProjectQuery(projectId, archived),
            ct);
}
