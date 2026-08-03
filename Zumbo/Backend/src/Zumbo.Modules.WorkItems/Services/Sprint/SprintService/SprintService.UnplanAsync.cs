using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    public async Task<SprintPlannedItemResponse> UnplanAsync(
        string sprintId,
        string workItemId,
        string correlationId,
        CancellationToken ct)
    {
        var initialSprint = await GetSprintAsync(sprintId, ct);
        await EnsurePermissionAsync(initialSprint.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        await using var projectLock = await AcquireProjectLockAsync(initialSprint.ProjectId, ct);
        var sprint = await GetSprintAsync(sprintId, ct);
        EnsurePlanned(sprint);
        var item = await workItems.SelectAsync(x => x.Id == workItemId && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        if (item.ProjectId != sprint.ProjectId || item.SprintId != sprint.Id)
        {
            throw new ConflictException("WORK_ITEM_NOT_IN_SPRINT", "Work item is not planned in this sprint.");
        }

        item.SprintId = null;
        item.UpdatedAt = clock.UtcNow;
        await SaveWorkItemAsync(item, useRequestVersion: true, ct);
        await audit.WriteAsync(
            "SprintWorkItemUnplanned",
            "WorkItem",
            item.Id,
            sprint.Id,
            null,
            correlationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return new SprintPlannedItemResponse(item.Id, null, item.EstimatePoints, item.Version);
    }
}
