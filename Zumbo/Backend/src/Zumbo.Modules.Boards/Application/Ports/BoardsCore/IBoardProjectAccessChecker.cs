using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;
public interface IBoardProjectAccessChecker
{
    Task EnsureCanAsync(string userId, string projectId, string permission, CancellationToken ct);
}
