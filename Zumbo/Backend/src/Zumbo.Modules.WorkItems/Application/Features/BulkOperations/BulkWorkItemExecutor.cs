using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.BulkOperations;

internal static class BulkWorkItemExecutor
{
    internal static async Task<BulkWorkItemResponse> ExecuteAsync<T>(
        IReadOnlyCollection<string> workItemIds,
        Func<string, string, CancellationToken, Task<T>> operation,
        string correlationId,
        CancellationToken ct)
    {
        if (workItemIds is null || workItemIds.Count is < 1 or > 100)
        {
            throw new ValidationException("Bulk work item operations require between 1 and 100 ids.");
        }

        var ids = workItemIds.Select(id => id?.Trim() ?? string.Empty).ToList();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
        {
            throw new ValidationException("Bulk work item ids must be non-empty and unique.");
        }

        var results = new List<BulkWorkItemResult>(ids.Count);
        for (var index = 0; index < ids.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var id = ids[index];
            try
            {
                await operation(id, $"{correlationId}:{index + 1}", ct);
                results.Add(new BulkWorkItemResult(id, true, null, null));
            }
            catch (ZumboException exception)
            {
                results.Add(new BulkWorkItemResult(id, false, exception.Code, exception.Message));
            }
        }

        var succeeded = results.Count(result => result.Success);
        return new BulkWorkItemResponse(results, succeeded, results.Count - succeeded);
    }
}
