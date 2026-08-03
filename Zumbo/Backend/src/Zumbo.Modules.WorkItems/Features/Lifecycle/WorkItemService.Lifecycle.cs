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
    public async Task ArchiveAsync(string id, string correlationId, CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemDelete", ct);
        await EnsureHasNoActiveChildrenAsync(workItem.Id, ct);
        if (wipProjection is not null)
        {
            await wipProjection.ReleaseAsync(workItem, ct);
        }
        workItem.Archived = true;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.DeleteAsync(workItem.Id, ct);
        await audit.WriteAsync("WorkItemArchived", "WorkItem", workItem.Id, "active", "archived", correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemArchived", "Work item archived", correlationId, ct);
        await PublishRealtimeAsync("archived", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
    }

    public async Task<WorkItemResponse> RestoreAsync(string id, string correlationId, CancellationToken ct)
    {
        var initialWorkItem = await GetArchivedWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetArchivedWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemDelete", ct);

        var placement = await boardPlacementPolicy.EnsureCanMoveAsync(
            workItem.ProjectId,
            workItem.BoardId,
            workItem.Id,
            workItem.Status,
            ct);
        await using (await AcquirePlacementLockAsync(workItem.BoardId, placement, ct))
        {
            if (wipProjection is null)
            {
                await boardPlacementPolicy.EnsureHasCapacityAsync(workItem.BoardId, placement.ColumnId, workItem.Id, ct);
            }
            else
            {
                await wipProjection.ReserveCreateAsync(workItem.ProjectId, workItem.BoardId, placement, ct);
            }
            workItem.ColumnId = placement.ColumnId;
            workItem.Status = placement.Status;
            workItem.Rank = await ranks.NextRankAsync(workItem.BoardId, placement.ColumnId, workItem.Id, ct);
            workItem.Archived = false;
            workItem.UpdatedAt = clock.UtcNow;
            await SaveAsync(workItem, ct);
        }

        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemRestored", "WorkItem", workItem.Id, "archived", "active", correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemRestored", "Work item restored", correlationId, ct);
        await PublishRealtimeAsync("restored", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public Task<BulkWorkItemResponse> BulkMoveAsync(BulkMoveWorkItemsRequest request, string correlationId, CancellationToken ct) =>
        ExecuteBulkAsync(
            request.WorkItemIds,
            (id, itemCorrelationId, token) => MoveAsync(id, new MoveWorkItemRequest(request.Status), itemCorrelationId, token),
            correlationId,
            ct);

    public Task<BulkWorkItemResponse> BulkAssignAsync(BulkAssignWorkItemsRequest request, string correlationId, CancellationToken ct) =>
        ExecuteBulkAsync(
            request.WorkItemIds,
            (id, itemCorrelationId, token) => AssignAsync(id, new AssignWorkItemRequest(request.AssigneeUserId), itemCorrelationId, token),
            correlationId,
            ct);

    public Task<BulkWorkItemResponse> BulkArchiveAsync(BulkArchiveWorkItemsRequest request, string correlationId, CancellationToken ct) =>
        ExecuteBulkAsync(
            request.WorkItemIds,
            async (id, itemCorrelationId, token) =>
            {
                await ArchiveAsync(id, itemCorrelationId, token);
                return true;
            },
            correlationId,
            ct);

    private async Task RecordActivityAndNotifyWatchersAsync(
        WorkItemDocument workItem,
        string activityType,
        string detail,
        string eventId,
        CancellationToken ct)
    {
        if (collaborationService is null)
        {
            return;
        }

        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        await collaborationService.RecordActivityAsync(
            workItem,
            organizationId,
            activityType,
            detail,
            eventId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title}: {detail}",
            eventId,
            null,
            ct);
    }

    private static string MutationEventId(WorkItemDocument workItem, string discriminator) =>
        $"{discriminator}:{workItem.UpdatedAt.ToUniversalTime().Ticks}";

    private Task PublishRealtimeAsync(
        string eventType,
        WorkItemDocument workItem,
        string correlationId,
        CancellationToken ct) =>
        realtimePublisher.PublishAsync(
            new WorkItemRealtimeChange(
                eventType,
                workItem.Id,
                workItem.ProjectId,
                workItem.BoardId,
                ToRealtimeItem(workItem),
                correlationId,
                clock.UtcNow,
                WorkItemRealtimeProtocol.CurrentSchemaVersion,
                workItem.Version),
            ct);
}
