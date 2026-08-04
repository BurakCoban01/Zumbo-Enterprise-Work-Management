using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private async Task ValidateExistingItemsAsync(WorkItemTypeSchemaDocument candidate, CancellationToken ct)
    {
        var batches = 0;
        string? cursor = null;
        do
        {
            if (++batches > MaxBatches)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_VALIDATION_LIMIT",
                    "Work item schema validation exceeded the configured batch limit.");
            }

            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == candidate.ProjectId && !item.Archived,
                cursor,
                BatchSize,
                ct);
            foreach (var item in page.Items)
            {
                var type = FindActiveIssueType(candidate, item.Type);
                ValidateStoredValues(candidate, type.Key, item.CustomFields);
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);
    }
}
