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
    public async Task<WorkItemResponse> AddWorkLogAsync(string id, AddWorkLogRequest request, CancellationToken ct)
    {
        if (request.Hours <= 0 || request.Hours > 24)
        {
            throw new ValidationException("Work log hours must be between 0 and 24.");
        }

        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkLogCreate", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var workLog = new WorkLogDocument
        {
            UserId = request.UserId,
            Hours = request.Hours,
            Note = request.Note,
            CreatedAt = clock.UtcNow
        };
        await activityStore.CreateWorkLogAsync(
            WorkItemActivityStore.ToActivity(workItem, CurrentOrganizationId(workItem.ProjectId), workLog),
            ct);
        workItem.WorkLogs.Add(workLog);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemWorkLogAdded", "Work log added", workLog.Id, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetParentAsync(
        string id,
        SetWorkItemParentRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        var parent = await ValidateParentAsync(
            workItem.ProjectId,
            workItem.BoardId,
            workItem.Type,
            request.ParentId,
            workItem.Id,
            ct);
        var oldParentId = workItem.ParentId;

        if (string.Equals(oldParentId, parent?.Id, StringComparison.Ordinal))
        {
            throw new ConflictException("WORK_ITEM_PARENT_UNCHANGED", "Work item already has the requested parent.");
        }

        workItem.ParentId = parent?.Id;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync("WorkItemParentChanged", "WorkItem", workItem.Id, oldParentId, parent?.Id, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemParentChanged", "Parent changed", correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> LinkAsync(
        string id,
        LinkWorkItemRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemLink", ct);
        var relationType = NormalizeRelationType(request.RelationType);

        if (string.Equals(workItem.Id, request.RelatedWorkItemId, StringComparison.Ordinal))
        {
            throw new ValidationException("A work item cannot be linked to itself.");
        }

        var relatedWorkItem = await GetWorkItem(request.RelatedWorkItemId, ct);
        if (!string.Equals(workItem.ProjectId, relatedWorkItem.ProjectId, StringComparison.Ordinal))
        {
            throw new ValidationException("Linked work items must belong to the same project.");
        }

        if (workItem.Relations.Any(x =>
            x.RelatedWorkItemId == relatedWorkItem.Id
            && x.RelationType.Equals(relationType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("WORK_ITEM_RELATION_EXISTS", "Work item relation already exists.");
        }

        await graph.AddRelationAsync(
            workItem.ProjectId,
            workItem.Id,
            relatedWorkItem.Id,
            relationType,
            ct);

        workItem.Relations.Add(new WorkItemRelationDocument
        {
            RelatedWorkItemId = relatedWorkItem.Id,
            RelationType = relationType
        });
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync("WorkItemLinked", "WorkItem", workItem.Id, null, $"{relationType}:{relatedWorkItem.Id}", correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemLinked", "Relation added", correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> UnlinkAsync(
        string id,
        string relatedWorkItemId,
        string relationType,
        string correlationId,
        CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemLink", ct);
        var normalizedType = NormalizeRelationType(relationType);
        var removed = workItem.Relations.RemoveAll(x =>
            x.RelatedWorkItemId == relatedWorkItemId
            && x.RelationType.Equals(normalizedType, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            throw new NotFoundException("WORK_ITEM_RELATION_NOT_FOUND", "Work item relation was not found.");
        }

        await graph.RemoveRelationAsync(
            workItem.ProjectId,
            workItem.Id,
            relatedWorkItemId,
            normalizedType,
            ct);
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync("WorkItemUnlinked", "WorkItem", workItem.Id, $"{normalizedType}:{relatedWorkItemId}", null, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemUnlinked", "Relation removed", correlationId, ct);
        return ToResponse(workItem);
    }
}
