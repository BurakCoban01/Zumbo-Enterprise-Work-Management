using Zumbo.Modules.WorkItems.Application.Features.BulkOperations;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    private async Task<BulkWorkItemResponse> ExecuteBulkAsync<T>(
        IReadOnlyCollection<string> workItemIds,
        Func<string, string, CancellationToken, Task<T>> operation,
        string correlationId,
        CancellationToken ct) =>
        await BulkWorkItemExecutor.ExecuteAsync(workItemIds, operation, correlationId, ct);
}
