using Zumbo.Modules.Boards.Application.Features.Lifecycle;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService
{
    public Task ArchiveAsync(string boardId, CancellationToken ct) => ArchiveAsync(boardId, "none", ct);

    public async Task ArchiveAsync(string boardId, string correlationId, CancellationToken ct)
        => await archiveBoardHandler.HandleAsync(new ArchiveBoardCommand(boardId, correlationId), ct);

    public Task<BoardResponse> RestoreAsync(string boardId, CancellationToken ct) =>
        RestoreAsync(boardId, "none", ct);

    public async Task<BoardResponse> RestoreAsync(string boardId, string correlationId, CancellationToken ct)
        => await restoreBoardHandler.HandleAsync(new RestoreBoardCommand(boardId, correlationId), ct);
}
