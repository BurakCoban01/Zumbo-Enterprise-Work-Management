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
    private async Task<WorkItemDocument?> ValidateParentAsync(
        string projectId,
        string boardId,
        string type,
        string? parentId,
        string? workItemId,
        CancellationToken ct)
    {
        var hierarchyLevel = await typeSchemas.HierarchyLevelAsync(projectId, type, ct);
        if (string.IsNullOrWhiteSpace(parentId))
        {
            if (hierarchyLevel == IssueTypeHierarchyLevels.Subtask)
            {
                throw new ValidationException("A subtask must have a parent work item.");
            }

            return null;
        }

        if (hierarchyLevel == IssueTypeHierarchyLevels.Epic)
        {
            throw new ValidationException("An epic cannot have a parent work item.");
        }

        if (string.Equals(parentId, workItemId, StringComparison.Ordinal))
        {
            throw new ValidationException("A work item cannot be its own parent.");
        }

        var parent = await GetWorkItem(parentId, ct);
        if (!string.Equals(parent.ProjectId, projectId, StringComparison.Ordinal))
        {
            throw new ValidationException("A parent work item must belong to the same project.");
        }

        if (parent.CompletedAt is not null || IsCompletedStatus(parent.Status))
        {
            throw new ConflictException("WORK_ITEM_PARENT_COMPLETED", "A completed work item cannot receive a child.");
        }

        var parentHierarchy = await typeSchemas.HierarchyLevelAsync(projectId, parent.Type, ct);
        if (hierarchyLevel == IssueTypeHierarchyLevels.Subtask)
        {
            if (parentHierarchy != IssueTypeHierarchyLevels.Standard)
            {
                throw new ValidationException("A subtask parent must be a story, task or bug.");
            }

            if (!string.Equals(parent.BoardId, boardId, StringComparison.Ordinal))
            {
                throw new ValidationException("A subtask and its parent must belong to the same board.");
            }
        }
        else if (parentHierarchy != IssueTypeHierarchyLevels.Epic)
        {
            throw new ValidationException("A story, task or bug can only be parented by an epic.");
        }

        await graph.EnsureCanSetParentAsync(projectId, workItemId, parent.Id, ct);

        return parent;
    }

    private async Task EnsureCanCompleteAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        await EnsureHasNoActiveChildrenAsync(workItem.Id, ct);
        var blockers = await graph.ActiveBlockerIdsAsync(workItem.ProjectId, workItem.Id, ct);

        if (blockers.Count > 0)
        {
            throw new ConflictException(
                "WORK_ITEM_BLOCKED",
                $"Work item cannot be completed while blockers remain active: {string.Join(", ", blockers)}.");
        }
    }

    private async Task EnsureHasNoActiveChildrenAsync(string workItemId, CancellationToken ct)
    {
        var activeChild = await workItems.SelectAsync(
            x => x.ParentId == workItemId && !x.Archived && x.CompletedAt == null && x.Status != "Done" && x.Status != "Closed",
            ct);
        if (activeChild is not null)
        {
            throw new ConflictException(
                "WORK_ITEM_HAS_ACTIVE_CHILDREN",
                "Work item cannot be completed or archived while it has active children.");
        }
    }
}
