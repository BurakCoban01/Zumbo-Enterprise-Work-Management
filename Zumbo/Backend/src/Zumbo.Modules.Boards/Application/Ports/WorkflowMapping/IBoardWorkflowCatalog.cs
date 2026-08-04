using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public interface IBoardWorkflowCatalog
{
    Task EnsureStatusesAvailableAsync(string projectId, IReadOnlyCollection<string> statuses, CancellationToken ct);
}
