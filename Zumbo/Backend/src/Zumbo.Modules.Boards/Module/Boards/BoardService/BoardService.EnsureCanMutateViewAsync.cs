using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    private async Task EnsureCanMutateViewAsync(
        BoardDocument board,
        BoardViewDocument view,
        bool targetIsShared,
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (!view.IsShared && view.OwnerUserId != userId)
        {
            throw new NotFoundException("BOARD_VIEW_NOT_FOUND", "Board view was not found.");
        }

        await EnsurePermissionAsync(
            board.ProjectId,
            view.IsShared || targetIsShared ? "BoardManage" : "BoardView",
            ct);
    }
}
