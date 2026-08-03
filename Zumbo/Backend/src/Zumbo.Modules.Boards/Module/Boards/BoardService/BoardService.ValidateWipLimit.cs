using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    private static void ValidateWipLimit(int? wipLimit)
    {
        if (wipLimit is < 1 or > 1000)
        {
            throw new ValidationException("WIP limit must be between 1 and 1000 when provided.");
        }
    }
}
