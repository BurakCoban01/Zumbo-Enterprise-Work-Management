using Zumbo.Modules.Boards.Application.Features.BoardsCore;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService
{
    public Task<BoardResponse> CreateAsync(CreateBoardRequest request, CancellationToken ct) =>
        CreateAsync(request, "none", ct);

    public async Task<BoardResponse> CreateAsync(CreateBoardRequest request, string correlationId, CancellationToken ct) =>
        await createBoardHandler.HandleAsync(request, correlationId, ct);

    public async Task<IReadOnlyList<BoardResponse>> ListByProjectAsync(
        string projectId,
        CancellationToken ct,
        bool archived = false) =>
        await listBoardsByProjectHandler.HandleAsync(
            new ListBoardsByProjectQuery(projectId, archived),
            ct);

    public Task<BoardResponse> UpdateAsync(string boardId, UpdateBoardRequest request, CancellationToken ct) =>
        UpdateAsync(boardId, request, "none", ct);

    public async Task<BoardResponse> UpdateAsync(string boardId, UpdateBoardRequest request, string correlationId, CancellationToken ct) =>
        await updateBoardHandler.HandleAsync(boardId, request, correlationId, ct);
}
