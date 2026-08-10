using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal sealed class GetSprintBurndownSlice(
    IDocumentRepository<SprintDocument> sprints,
    IDocumentRepository<SprintScopeSnapshotDocument> scopeSnapshots,
    IDocumentRepository<SprintCompletionSnapshotDocument> completionSnapshots,
    IDocumentRepository<WorkItemDocument> workItems,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    IOptions<SprintOptions> configuredOptions,
    IWorkItemReadModelCache readModelCache,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
{
    private int BatchSize => Math.Clamp(configuredOptions.Value.BatchSize, 1, 200);
    private int MaxBatches => Math.Clamp(configuredOptions.Value.MaxBatchesPerOperation, 1, 10_000);
    private TimeSpan ReadModelTtl => TimeSpan.FromSeconds(
        Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 1, 300));

    internal async Task<WorkItemReportSnapshot<IReadOnlyList<SprintBurndownPointResponse>>> HandleAsync(
        GetSprintBurndownQuery query,
        CancellationToken ct)
    {
        var sprint = await sprints.SelectAsync(item => item.Id == query.SprintId, ct)
            ?? throw new NotFoundException("SPRINT_NOT_FOUND", "Sprint was not found.");
        if (sprint.ProjectId != query.ProjectId)
        {
            throw new ConflictException("SPRINT_PROJECT_MISMATCH", "Sprint does not belong to the requested project.");
        }

        await EnsureViewAsync(query.ProjectId, ct);
        var sprintStart = DateOnly.FromDateTime(sprint.StartAtUtc.UtcDateTime);
        var sprintEnd = DateOnly.FromDateTime(sprint.EndAtUtc.UtcDateTime);
        var start = query.StartDate ?? sprintStart;
        var end = query.EndDate ?? sprintEnd;
        if (start < sprintStart || end > sprintEnd || end < start || end.DayNumber - start.DayNumber + 1 > 60)
        {
            throw new ValidationException("Burndown range must be within the sprint and cannot exceed 60 days.");
        }

        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<SprintBurndownPointResponse>>(
            query.ProjectId,
            $"sprint-burndown:{query.SprintId}:{start:yyyyMMdd}:{end:yyyyMMdd}",
            ReadModelTtl,
            async token =>
            {
                var dayCount = end.DayNumber - start.DayNumber + 1;
                var committedItems = 0;
                var committedPoints = 0m;
                var completedPointsByDay = new decimal[dayCount];
                var completedItemsByDay = new int[dayCount];
                var batches = 0;
                string? cursor = null;
                do
                {
                    EnsureBatchLimit(++batches);
                    var page = await scopeSnapshots.ListByCursorAsync(
                        snapshot => snapshot.SprintId == sprint.Id,
                        cursor,
                        BatchSize,
                        token);
                    foreach (var scope in page.Items)
                    {
                        committedItems++;
                        committedPoints += scope.EstimatePoints;
                        DateTimeOffset? completedAt;
                        if (sprint.Status == SprintStatuses.Completed)
                        {
                            completedAt = (await completionSnapshots.SelectAsync(
                                snapshot => snapshot.Id == scope.Id && snapshot.Completed,
                                token))?.CompletedAt;
                        }
                        else
                        {
                            completedAt = (await workItems.SelectAsync(
                                item => item.Id == scope.WorkItemId,
                                token))?.CompletedAt;
                        }

                        if (completedAt is not null)
                        {
                            var date = DateOnly.FromDateTime(completedAt.Value.UtcDateTime);
                            if (date <= end)
                            {
                                var offset = Math.Max(0, date.DayNumber - start.DayNumber);
                                completedPointsByDay[offset] += scope.EstimatePoints;
                                completedItemsByDay[offset]++;
                            }
                        }
                    }

                    cursor = page.NextCursor;
                }
                while (cursor is not null);

                var result = new List<SprintBurndownPointResponse>(dayCount);
                var completedPoints = 0m;
                var completedItems = 0;
                for (var offset = 0; offset < dayCount; offset++)
                {
                    completedPoints += completedPointsByDay[offset];
                    completedItems += completedItemsByDay[offset];
                    result.Add(new SprintBurndownPointResponse(
                        start.AddDays(offset),
                        committedPoints - completedPoints,
                        committedItems - completedItems));
                }

                return result;
            },
            ct);
    }

    private async Task EnsureViewAsync(string projectId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        _ = await permissionChecker.EnsureCanAsync(userId, projectId, PermissionCatalog.WorkItemView, ct);
    }

    private void EnsureBatchLimit(int batches)
    {
        if (batches > MaxBatches)
        {
            throw new ConflictException("SPRINT_BATCH_LIMIT", "Sprint operation exceeded its bounded batch limit.");
        }
    }
}
