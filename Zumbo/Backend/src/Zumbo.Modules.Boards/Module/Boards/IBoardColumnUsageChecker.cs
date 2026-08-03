using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public interface IBoardColumnUsageChecker
{
    Task<bool> HasWorkItemsAsync(string boardId, string columnId, string columnName, CancellationToken ct);
    Task<bool> HasBoardWorkItemsAsync(string boardId, CancellationToken ct);
    Task ValidateMappingAsync(BoardDocument board, CancellationToken ct);
}
