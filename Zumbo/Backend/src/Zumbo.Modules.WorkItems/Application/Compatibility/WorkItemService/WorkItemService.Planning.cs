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
    public async Task<WorkItemResponse> MoveAsync(string id, MoveWorkItemRequest request, string correlationId, CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemMove", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var target = request.Status.Trim();
        var aggregate = WorkItemAggregate.Rehydrate(workItem);
        aggregate.EnsureCanTarget(target);

        var rule = await workflowPolicy.EnsureTransitionAllowedAsync(workItem.ProjectId, workItem.Type, workItem.Status, target, ct);
        var preparedTransition = aggregate.PrepareTransition(rule, clock.UtcNow);

        var placement = await boardPlacementPolicy.EnsureCanMoveAsync(
            workItem.ProjectId,
            workItem.BoardId,
            workItem.Id,
            target,
            ct);
        var targetRank = await ranks.NextRankAsync(workItem.BoardId, placement.ColumnId, workItem.Id, ct);

        if (rule.ToStatusCategory.Equals("Done", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureCanCompleteAsync(workItem, ct);
        }

        var oldStatus = string.Empty;
        await using (await AcquirePlacementLockAsync(workItem.BoardId, placement, ct))
        {
            if (wipProjection is null)
            {
                await boardPlacementPolicy.EnsureHasCapacityAsync(workItem.BoardId, placement.ColumnId, workItem.Id, ct);
            }
            else
            {
                await wipProjection.ReserveMoveAsync(workItem, placement, ct);
            }
            oldStatus = workItem.Status;
            var now = clock.UtcNow;
            aggregate.Move(
                rule,
                placement,
                targetRank,
                preparedTransition,
                now,
                currentUser.UserId ?? "system");
            await SaveAsync(workItem, ct);
            foreach (var approval in workItem.Approvals)
            {
                await UpdateApprovalActivityAsync(workItem, approval, ct);
            }
            await activityStore.CreateTimelineAsync(
                WorkItemActivityStore.ToActivity(
                    workItem,
                    CurrentOrganizationId(workItem.ProjectId),
                    workItem.StatusHistory[^1],
                    workItem.StatusHistory.Count - 1),
                ct);
        }
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemMoved", "WorkItem", workItem.Id, oldStatus, placement.Status, correlationId, ct);
        if (collaborationService is not null)
        {
            var organizationId = CurrentOrganizationId(workItem.ProjectId);
            await collaborationService.RecordActivityAsync(
                workItem,
                organizationId,
                "WorkItemMoved",
                $"{oldStatus} -> {placement.Status}",
                correlationId,
                ct);
            await collaborationService.NotifyWatchersAsync(
                workItem,
                organizationId,
                "WatcherStatus",
                $"{workItem.Title} moved to {placement.Status}",
                correlationId,
                null,
                ct);
        }
        if (rule.Automations?.Count > 0)
        {
            await audit.WriteAsync(
                "WorkItemAutomationApplied",
                "WorkItem",
                workItem.Id,
                null,
                string.Join(',', rule.Automations.Select(x => x.Action)),
                correlationId,
                ct);
        }
        await PublishRealtimeAsync("moved", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await PublishAutomationAsync(
            "WorkItemTransitioned",
            workItem,
            oldStatus,
            correlationId,
            $"transitioned:{workItem.Version}",
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> ReorderAsync(
        string id,
        ReorderWorkItemRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemMove", ct);

        var rank = await ranks.ResolveReorderRankAsync(workItem, request, ct);
        var oldRank = workItem.Rank;
        workItem.Rank = rank;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync(
            "WorkItemReordered",
            "WorkItem",
            workItem.Id,
            oldRank.ToString(System.Globalization.CultureInfo.InvariantCulture),
            rank.ToString(System.Globalization.CultureInfo.InvariantCulture),
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemReordered", "Rank changed", correlationId, ct);
        await PublishRealtimeAsync("reordered", workItem, correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetPlanningAsync(string id, SetWorkItemPlanningRequest request, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        await (sprintPolicy ?? new NoOpWorkItemSprintPolicy()).EnsurePlanningAllowedAsync(
            workItem.ProjectId,
            workItem.SprintId,
            request.SprintId,
            ct);
        var aggregate = WorkItemAggregate.Rehydrate(workItem);
        aggregate.Plan(request.SprintId, request.EstimatePoints, clock.UtcNow);
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemPlanningUpdated",
            "Planning updated",
            MutationEventId(workItem, "planning"),
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> AddChecklistItemAsync(string id, AddChecklistItemRequest request, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        workItem.Checklist.Add(new ChecklistItemDocument { Text = request.Text.Trim() });
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        var checklistItem = workItem.Checklist[^1];
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemChecklistItemAdded", "Checklist item added", checklistItem.Id, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> CompleteChecklistItemAsync(string id, string itemId, CompleteChecklistItemRequest request, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        var item = workItem.Checklist.SingleOrDefault(x => x.Id == itemId)
            ?? throw new NotFoundException("CHECKLIST_ITEM_NOT_FOUND", "Checklist item was not found.");
        item.Completed = request.Completed;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemChecklistItemUpdated",
            request.Completed ? "Checklist item completed" : "Checklist item reopened",
            MutationEventId(workItem, "checklist:" + itemId),
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> AddLabelAsync(string id, AddLabelRequest request, CancellationToken ct)
    {
        var label = request.Label.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ValidationException("Label is required.");
        }

        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        if (workItem.Labels.Any(x => x.Equals(label, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("WORK_ITEM_LABEL_EXISTS", "Work item already has this label.");
        }

        workItem.Labels.Add(label);
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemLabelAdded", "Label added", MutationEventId(workItem, "label:add:" + label), ct);
        await PublishAutomationAsync(
            "WorkItemUpdated",
            workItem,
            workItem.Status,
            MutationEventId(workItem, "label:add:" + label),
            $"label-added:{workItem.Version}:{label}",
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> RemoveLabelAsync(string id, string label, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        var removed = workItem.Labels.RemoveAll(x => x.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            throw new NotFoundException("WORK_ITEM_LABEL_NOT_FOUND", "Work item label was not found.");
        }

        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemLabelRemoved", "Label removed", MutationEventId(workItem, "label:remove:" + label), ct);
        await PublishAutomationAsync(
            "WorkItemUpdated",
            workItem,
            workItem.Status,
            MutationEventId(workItem, "label:remove:" + label),
            $"label-removed:{workItem.Version}:{label}",
            ct);
        return ToResponse(workItem);
    }
}
