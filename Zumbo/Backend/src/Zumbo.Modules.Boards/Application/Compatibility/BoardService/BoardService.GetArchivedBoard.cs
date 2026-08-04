using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    private async Task<BoardDocument> GetArchivedBoard(string boardId, CancellationToken ct) =>
        await boards.SelectAsync(x => x.Id == boardId && x.Archived, ct)
        ?? throw new NotFoundException("BOARD_NOT_FOUND", "Archived board was not found.");
}
