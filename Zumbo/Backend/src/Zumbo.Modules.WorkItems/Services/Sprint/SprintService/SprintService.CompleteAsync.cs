using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    public async Task<SprintResponse> CompleteAsync(
        string sprintId,
        CompleteSprintRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var initial = await GetSprintAsync(sprintId, ct);
        await EnsurePermissionAsync(initial.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        await using var projectLock = await AcquireProjectLockAsync(initial.ProjectId, ct);
        var sprint = await GetSprintAsync(sprintId, ct);
        if (sprint.Status != SprintStatuses.Active)
        {
            throw new ConflictException("SPRINT_COMPLETE_INVALID_STATE", "Only an active sprint can be completed.");
        }

        var carryoverId = NormalizeOptional(request.CarryoverSprintId);
        if (carryoverId == sprint.Id)
        {
            throw new ValidationException("Carryover sprint must be different from the completed sprint.");
        }

        SprintDocument? carryover = null;
        if (carryoverId is not null)
        {
            carryover = await GetSprintAsync(carryoverId, ct);
            if (carryover.ProjectId != sprint.ProjectId)
            {
                throw new ConflictException("SPRINT_PROJECT_MISMATCH", "Carryover sprint must belong to the same project.");
            }

            EnsurePlanned(carryover);
        }

        var now = clock.UtcNow;
        var completedItems = 0;
        var completedPoints = 0m;
        var carryoverItems = 0;
        var carryoverPoints = 0m;
        var batches = 0;
        string? cursor = null;
        do
        {
            EnsureBatchLimit(++batches);
            var page = await scopeSnapshots.ListByCursorAsync(
                snapshot => snapshot.SprintId == sprint.Id,
                cursor,
                BatchSize,
                ct);
            foreach (var scope in page.Items)
            {
                var item = await workItems.SelectAsync(x => x.Id == scope.WorkItemId, ct)
                    ?? throw new ConflictException("SPRINT_SCOPE_ITEM_MISSING", "A committed sprint work item is missing.");
                var completed = item.CompletedAt is not null;
                var itemCarryoverId = !completed && !item.Archived ? carryover?.Id : null;
                await completionSnapshots.CreateAsync(new SprintCompletionSnapshotDocument
                {
                    Id = SnapshotId(sprint.Id, scope.WorkItemId),
                    SprintId = sprint.Id,
                    ProjectId = sprint.ProjectId,
                    WorkItemId = scope.WorkItemId,
                    CommittedPoints = scope.EstimatePoints,
                    Completed = completed,
                    CompletedAt = item.CompletedAt,
                    CarryoverSprintId = itemCarryoverId,
                    CapturedAt = now
                }, ct);
                if (completed)
                {
                    completedItems++;
                    completedPoints += scope.EstimatePoints;
                }
                else if (itemCarryoverId is not null)
                {
                    item.SprintId = itemCarryoverId;
                    item.UpdatedAt = now;
                    await SaveWorkItemAsync(item, useRequestVersion: false, ct);
                    carryoverItems++;
                    carryoverPoints += scope.EstimatePoints;
                }
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        SprintAggregate.Rehydrate(sprint).Complete(
            completedItems,
            completedPoints,
            carryoverItems,
            carryoverPoints,
            now);
        await SaveSprintAsync(sprint, ct);
        await audit.WriteAsync(
            "SprintCompleted",
            "Sprint",
            sprint.Id,
            SprintStatuses.Active,
            $"{completedItems}|{completedPoints}|{carryoverItems}|{carryoverPoints}",
            correlationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return ToResponse(sprint);
    }
}
