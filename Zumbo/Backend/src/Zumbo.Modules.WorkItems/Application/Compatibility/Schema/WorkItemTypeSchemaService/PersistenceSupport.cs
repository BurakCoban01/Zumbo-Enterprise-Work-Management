using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Schema;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService
{
private async Task<IAsyncDisposable> AcquireProjectLockAsync(string projectId, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
                "work-item-schema:" + projectId,
                TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
                TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
                ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The work item schema is busy; retry the operation.");
    }

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

private async Task EnsurePermissionAsync(string projectId, string permission, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }
        _ = await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
    }

private async Task<WorkItemTypeSchemaDocument> LoadOrDefaultAsync(string projectId, CancellationToken ct) =>
        await schemas.SelectAsync(schema => schema.ProjectId == projectId, ct) ?? Default(projectId, clock.UtcNow);

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
