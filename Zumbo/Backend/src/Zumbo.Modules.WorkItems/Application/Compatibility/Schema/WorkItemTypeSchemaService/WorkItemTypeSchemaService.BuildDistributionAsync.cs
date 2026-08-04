using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private async Task<WorkItemFieldDistributionResponse> BuildDistributionAsync(
        string projectId,
        string field,
        Func<WorkItemDocument, string?> selector,
        CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var missing = 0;
        string? cursor = null;
        for (var batch = 0; ; batch++)
        {
            if (batch >= MaxBatches)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_REPORT_LIMIT",
                    "Work item field report exceeded the configured batch limit.");
            }

            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == projectId && !item.Archived,
                cursor,
                BatchSize,
                ct);
            foreach (var item in page.Items)
            {
                total++;
                var value = selector(item);
                if (string.IsNullOrWhiteSpace(value))
                {
                    missing++;
                }
                else
                {
                    counts[value] = counts.GetValueOrDefault(value) + 1;
                }
            }

            cursor = page.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        return new WorkItemFieldDistributionResponse(
            projectId,
            field,
            total,
            missing,
            counts.OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new WorkItemFieldDistributionEntry(item.Key, item.Value))
                .ToList());
    }
}
