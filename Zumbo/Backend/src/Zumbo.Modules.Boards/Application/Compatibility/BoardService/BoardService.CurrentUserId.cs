using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService{

    private string CurrentUserId() =>
        !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : throw new UnauthorizedException("Authenticated user is required.");
}
