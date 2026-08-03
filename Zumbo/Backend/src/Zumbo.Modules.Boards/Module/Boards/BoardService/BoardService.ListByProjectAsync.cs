using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    public async Task<IReadOnlyList<BoardResponse>> ListByProjectAsync(
        string projectId,
        CancellationToken ct,
        bool archived = false)
    {
        var normalizedProjectId = projectId.Trim();
        await EnsurePermissionAsync(normalizedProjectId, "BoardView", ct);
        var result = await boards.ListByFilterAsync(
            x => x.ProjectId == normalizedProjectId && x.Archived == archived,
            x => x.Name,
            pageSize: 100,
            cancellationToken: ct);

        return result.Select(ToResponse).ToList();
    }
}
