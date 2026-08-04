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

public sealed partial class WorkItemService
{
    private async Task<BulkWorkItemResponse> ExecuteBulkAsync<T>(
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
