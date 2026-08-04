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
    private TimeSpan ReadModelTtl =>
        TimeSpan.FromSeconds(Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 5, 300));

    private static string NormalizeRelationType(string? relationType)
    {
        var requested = string.IsNullOrWhiteSpace(relationType) ? "RelatesTo" : relationType.Trim();
        return requested.ToLowerInvariant() switch
        {
            "blocks" => "Blocks",
            "blockedby" or "blocked-by" => "BlockedBy",
            "relatesto" or "relates-to" => "RelatesTo",
            "duplicates" => "Duplicates",
            _ => throw new ValidationException("Relation type must be Blocks, BlockedBy, RelatesTo or Duplicates.")
        };
    }

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<WorkItemDocument> GetWorkItem(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(x => x.Id == id && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemView, ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        return workItem;
    }

    private async Task<WorkItemDocument> GetArchivedWorkItem(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(x => x.Id == id && x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Archived work item was not found.");
        var authorization = await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemView, ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        return workItem;
    }

    private async Task SaveAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        await activityStore.MigrateEmbeddedAsync(workItem, CurrentOrganizationId(workItem.ProjectId), ct);
        var comments = workItem.Comments;
        var attachments = workItem.Attachments;
        var workLogs = workItem.WorkLogs;
        var approvals = workItem.Approvals;
        var statusHistory = workItem.StatusHistory;
        workItem.Comments = [];
        workItem.Attachments = [];
        workItem.WorkLogs = [];
        workItem.Approvals = [];
        workItem.StatusHistory = [];
        try
        {
            var result = await workItems.ReplaceByVersionAsync(
                x => x.Id == workItem.Id,
                workItem,
                expectedVersion.Consume(workItem.Version),
                ct);
            if (!result.Found)
            {
                throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
            }

            workItem.Version = result.Version!.Value;
        }
        finally
        {
            workItem.Comments = comments;
            workItem.Attachments = attachments;
            workItem.WorkLogs = workLogs;
            workItem.Approvals = approvals;
            workItem.StatusHistory = statusHistory;
        }
    }

    private async Task EnsureSeparatedAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        if (workItem.ActivityStorageVersion >= 1)
        {
            return;
        }

        await activityStore.MigrateEmbeddedAsync(workItem, CurrentOrganizationId(workItem.ProjectId), ct);
        await SaveAsync(workItem, ct);
    }

    private async Task HydrateAllAsync(IEnumerable<WorkItemDocument> source, CancellationToken ct)
    {
        foreach (var workItem in source)
        {
            await activityStore.HydrateAsync(workItem, CurrentOrganizationId(workItem.ProjectId), ct);
        }
    }
}
