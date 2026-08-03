using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    private static void EnsureUniqueViewName(
        BoardDocument board,
        string name,
        string ownerUserId,
        bool isShared,
        string? ignoredViewId = null)
    {
        if (board.Views.Any(x =>
            x.Id != ignoredViewId
            && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && (isShared || x.IsShared || x.OwnerUserId == ownerUserId)))
        {
            throw new ConflictException("BOARD_VIEW_NAME_EXISTS", "Board view name must be unique in its visibility scope.");
        }
    }
}
