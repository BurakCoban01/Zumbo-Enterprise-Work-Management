using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    public async Task<SprintPlannedItemResponse> PlanAsync(
        string sprintId,
        string workItemId,
        PlanSprintWorkItemRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var estimate = NormalizeEstimate(request.EstimatePoints);
        var initialSprint = await GetSprintAsync(sprintId, ct);
        await EnsurePermissionAsync(initialSprint.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        await using var projectLock = await AcquireProjectLockAsync(initialSprint.ProjectId, ct);
        var sprint = await GetSprintAsync(sprintId, ct);
        EnsurePlanned(sprint);
        var item = await workItems.SelectAsync(x => x.Id == workItemId && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        if (item.ProjectId != sprint.ProjectId)
        {
            throw new ConflictException("SPRINT_PROJECT_MISMATCH", "Work item and sprint must belong to the same project.");
        }

        if (item.SprintId is not null && item.SprintId != sprint.Id)
        {
            throw new ConflictException("WORK_ITEM_ALREADY_PLANNED", "Work item is already planned in another sprint.");
        }

        item.SprintId = sprint.Id;
        item.EstimatePoints = estimate;
        item.UpdatedAt = clock.UtcNow;
        await SaveWorkItemAsync(item, useRequestVersion: true, ct);
        await audit.WriteAsync(
            "SprintWorkItemPlanned",
            "WorkItem",
            item.Id,
            null,
            sprint.Id,
            correlationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return new SprintPlannedItemResponse(item.Id, item.SprintId, item.EstimatePoints, item.Version);
    }
}
