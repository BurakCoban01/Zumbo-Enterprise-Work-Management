using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public interface IBoardPlacementPolicy
{
    Task<BoardPlacement> ResolveInitialAsync(string projectId, string boardId, CancellationToken ct);
    Task<BoardPlacement> EnsureCanMoveAsync(
        string projectId,
        string boardId,
        string workItemId,
        string targetStatus,
        CancellationToken ct);
    Task EnsureHasCapacityAsync(
        string boardId,
        string columnId,
        string? ignoredWorkItemId,
        CancellationToken ct);
}
