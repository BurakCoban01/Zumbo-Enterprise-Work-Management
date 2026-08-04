using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

internal sealed class ListBoardsByProjectSlice(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    ICurrentUser currentUser)
{
    internal async Task<IReadOnlyList<BoardResponse>> HandleAsync(
        ListBoardsByProjectQuery query,
        CancellationToken ct)
    {
        ListBoardsByProjectValidator.Validate(query);
        var normalizedProjectId = query.ProjectId.Trim();
        await EnsurePermissionAsync(normalizedProjectId, "BoardView", ct);
        var result = await boards.ListByFilterAsync(
            x => x.ProjectId == normalizedProjectId && x.Archived == query.Archived,
            x => x.Name,
            pageSize: 100,
            cancellationToken: ct);

        return result.Select(board => BoardResponseMapper.ToResponse(board, currentUser)).ToList();
    }

    private async Task EnsurePermissionAsync(string projectId, string permission, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        await accessChecker.EnsureCanAsync(userId, projectId, permission, ct);
    }
}
