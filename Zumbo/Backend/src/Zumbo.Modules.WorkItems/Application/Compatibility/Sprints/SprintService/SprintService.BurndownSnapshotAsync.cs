using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    public async Task<WorkItemReportSnapshot<IReadOnlyList<SprintBurndownPointResponse>>> BurndownSnapshotAsync(
        string projectId,
        string sprintId,
        DateOnly? requestedStart,
        DateOnly? requestedEnd,
        CancellationToken ct)
    {
        var sprint = await GetSprintAsync(sprintId, ct);
        if (sprint.ProjectId != projectId)
        {
            throw new ConflictException("SPRINT_PROJECT_MISMATCH", "Sprint does not belong to the requested project.");
        }

        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var sprintStart = DateOnly.FromDateTime(sprint.StartAtUtc.UtcDateTime);
        var sprintEnd = DateOnly.FromDateTime(sprint.EndAtUtc.UtcDateTime);
        var start = requestedStart ?? sprintStart;
        var end = requestedEnd ?? sprintEnd;
        if (start < sprintStart || end > sprintEnd || end < start || end.DayNumber - start.DayNumber + 1 > 60)
        {
            throw new ValidationException("Burndown range must be within the sprint and cannot exceed 60 days.");
        }

        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<SprintBurndownPointResponse>>(
            projectId,
            $"sprint-burndown:{sprintId}:{start:yyyyMMdd}:{end:yyyyMMdd}",
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
}
