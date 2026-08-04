using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    public async Task<SprintResponse> StartAsync(
        string sprintId,
        string correlationId,
        CancellationToken ct)
    {
        var initial = await GetSprintAsync(sprintId, ct);
        await EnsurePermissionAsync(initial.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        await using var projectLock = await AcquireProjectLockAsync(initial.ProjectId, ct);
        var sprint = await GetSprintAsync(sprintId, ct);
        EnsurePlanned(sprint);
        if (await sprints.ExistsByFilterAsync(
                item => item.ProjectId == sprint.ProjectId
                    && item.Status == SprintStatuses.Active
                    && item.Id != sprint.Id,
                ct))
        {
            throw new ConflictException("SPRINT_ACTIVE_EXISTS", "Only one sprint can be active in a project.");
        }

        var now = clock.UtcNow;
        var count = 0;
        var points = 0m;
        var batches = 0;
        string? cursor = null;
        do
        {
            EnsureBatchLimit(++batches);
            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == sprint.ProjectId
                    && item.SprintId == sprint.Id
                    && !item.Archived,
                cursor,
                BatchSize,
                ct);
            foreach (var item in page.Items)
            {
                await scopeSnapshots.CreateAsync(new SprintScopeSnapshotDocument
                {
                    Id = SnapshotId(sprint.Id, item.Id),
                    SprintId = sprint.Id,
                    ProjectId = sprint.ProjectId,
                    WorkItemId = item.Id,
                    Title = item.Title,
                    EstimatePoints = item.EstimatePoints,
                    CapturedAt = now
                }, ct);
                count++;
                points += item.EstimatePoints;
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        SprintAggregate.Rehydrate(sprint).Start(count, points, now);
        await SaveSprintAsync(sprint, ct);
        await audit.WriteAsync(
            "SprintStarted",
            "Sprint",
            sprint.Id,
            SprintStatuses.Planned,
            $"{count}|{points}",
            correlationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return ToResponse(sprint);
    }
}
