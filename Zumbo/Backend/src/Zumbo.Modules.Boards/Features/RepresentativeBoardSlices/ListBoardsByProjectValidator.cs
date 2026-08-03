using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed class ListBoardsByProjectValidator
{
    public static void Validate(ListBoardsByProjectQuery query) => ArgumentNullException.ThrowIfNull(query);
}
