using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    private static void EnsureUniqueColumn(
        BoardDocument board,
        string name,
        string category,
        string? ignoredColumnId = null)
    {
        if (board.Columns.Any(x => x.Id != ignoredColumnId && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_COLUMN_NAME_EXISTS", "Column name must be unique inside the board.");
        }

        if (category != "Custom" && board.Columns.Any(x =>
            x.Id != ignoredColumnId && x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("BOARD_COLUMN_CATEGORY_EXISTS", "A board can contain only one standard column per category.");
        }
    }
}
