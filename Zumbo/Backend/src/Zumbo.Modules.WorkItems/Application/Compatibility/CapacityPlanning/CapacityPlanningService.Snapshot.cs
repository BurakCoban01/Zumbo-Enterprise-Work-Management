using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private CapacitySnapshotResponse BuildSnapshot(
        CapacityPlanDocument plan,
        IReadOnlyCollection<CapacityAllocationDocument> allocations,
        CapacitySource source)
    {
        var start = DateOnlyUtc(plan.PeriodStartUtc);
        var end = DateOnlyUtc(plan.PeriodEndUtc);
        var weekStarts = Weeks(start, end);
        var memberRows = plan.Members.Select(member =>
        {
            var memberAllocations = allocations
                .Where(item => item.UserId == member.UserId)
                .ToList();
            var memberTasks = source.Tasks
                .Where(item => item.AssigneeUserId == member.UserId)
                .Select(ToTask)
                .ToList();
            var weeks = weekStarts.Select(weekStart =>
            {
                var weekEnd = weekStart.AddDays(6);
                var capacity = Round(member.WeeklyCapacityHours
                    * WorkingDays(Max(start, weekStart), Min(end, weekEnd)) / 5m);
                var allocated = Round(memberAllocations.Sum(allocation =>
                {
                    var allocationStart = DateOnlyUtc(allocation.StartDateUtc);
                    var allocationEnd = DateOnlyUtc(allocation.EndDateUtc);
                    var overlapStart = Max(Max(start, weekStart), allocationStart);
                    var overlapEnd = Min(Min(end, weekEnd), allocationEnd);
                    if (overlapEnd < overlapStart) return 0m;
                    return member.WeeklyCapacityHours
                        * WorkingDays(overlapStart, overlapEnd) / 5m
                        * allocation.Percent / 100m;
                }));
                var dueTasks = memberTasks
                    .Where(item => item.DueDate >= weekStart && item.DueDate <= weekEnd)
                    .ToList();
                return new CapacityWeekResponse(
                    weekStart,
                    capacity,
                    allocated,
                    Round(capacity - allocated),
                    Percent(allocated, capacity),
                    State(allocated, capacity),
                    Round(dueTasks.Sum(item => item.EstimatePoints ?? 0)),
                    dueTasks.Count(item => item.EstimatePoints is null),
                    dueTasks.Count);
            }).ToList();
            var capacityHours = Round(weeks.Sum(item => item.CapacityHours));
            var allocatedHours = Round(weeks.Sum(item => item.AllocatedHours));
            return new CapacityMemberSnapshotResponse(
                member.UserId,
                member.TeamId,
                member.WeeklyCapacityHours,
                capacityHours,
                allocatedHours,
                Round(capacityHours - allocatedHours),
                Percent(allocatedHours, capacityHours),
                State(allocatedHours, capacityHours),
                Round(memberTasks.Sum(item => item.EstimatePoints ?? 0)),
                memberTasks.Count(item => item.EstimatePoints is null),
                memberTasks.Count(item => item.DueDate is null),
                memberTasks.Count,
                weeks,
                memberTasks);
        }).ToList();

        var teamRows = memberRows
            .Where(item => item.TeamId is not null)
            .GroupBy(item => item.TeamId!, StringComparer.Ordinal)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var capacity = Round(group.Sum(item => item.CapacityHours));
                var allocated = Round(group.Sum(item => item.AllocatedHours));
                return new CapacityTeamSnapshotResponse(
                    group.Key,
                    group.Count(),
                    capacity,
                    allocated,
                    Round(capacity - allocated),
                    State(allocated, capacity),
                    group.Sum(item => item.OpenItems),
                    group.Sum(item => item.UnestimatedItems));
            }).ToList();

        var projectRows = source.Projects.Select(project =>
        {
            var projectAllocations = allocations
                .Where(item => item.ProjectId == project.Id)
                .ToList();
            var projectTasks = source.Tasks
                .Where(item => item.ProjectId == project.Id)
                .ToList();
            var allocatedHours = memberRows.Sum(member =>
                member.Weeks.Sum(week =>
                {
                    var memberAllocation = projectAllocations
                        .Where(item => item.UserId == member.UserId)
                        .Sum(item =>
                        {
                            var weekEnd = week.WeekStart.AddDays(6);
                            var allocationStart = DateOnlyUtc(item.StartDateUtc);
                            var allocationEnd = DateOnlyUtc(item.EndDateUtc);
                            var overlapStart = Max(Max(start, week.WeekStart), allocationStart);
                            var overlapEnd = Min(Min(end, weekEnd), allocationEnd);
                            if (overlapEnd < overlapStart) return 0m;
                            return member.WeeklyCapacityHours
                                * WorkingDays(overlapStart, overlapEnd) / 5m
                                * item.Percent / 100m;
                        });
                    return memberAllocation;
                }));
            return new CapacityProjectSnapshotResponse(
                project.Id,
                project.Key,
                project.Name,
                projectAllocations.Select(item => item.UserId).Distinct(StringComparer.Ordinal).Count(),
                Round(allocatedHours),
                projectTasks.Count,
                Round(projectTasks.Sum(item => item.EstimatePoints)),
                projectTasks.Count(item => item.EstimatePoints <= 0));
        }).ToList();

        var totalCapacity = Round(memberRows.Sum(item => item.CapacityHours));
        var totalAllocated = Round(memberRows.Sum(item => item.AllocatedHours));
        return new CapacitySnapshotResponse(
            plan.Id,
            plan.Version,
            source.UnavailableProjectIds.Count > 0 || source.Truncated
                ? CapacitySnapshotStatuses.Partial
                : CapacitySnapshotStatuses.Ready,
            start,
            end,
            clock.UtcNow,
            source.Truncated,
            source.UnavailableProjectIds,
            new CapacitySnapshotSummaryResponse(
                memberRows.Count,
                totalCapacity,
                totalAllocated,
                Round(totalCapacity - totalAllocated),
                memberRows.Count(item => item.State == CapacityLoadStates.OverCapacity),
                source.Tasks.Count,
                Round(source.Tasks.Sum(item => item.EstimatePoints)),
                source.Tasks.Count(item => item.EstimatePoints <= 0),
                source.Tasks.Count(item => item.DueDate is null)),
            memberRows,
            teamRows,
            projectRows);
    }

    private sealed record CapacitySource(
        IReadOnlyCollection<CapacityProjectAccess> Projects,
        IReadOnlyCollection<string> UnavailableProjectIds,
        IReadOnlyCollection<WorkItemDocument> Tasks,
        bool Truncated);

    private async Task<CapacitySource> LoadSourceAsync(
        CapacityPlanDocument plan,
        (string UserId, string OrganizationId) actor,
        CancellationToken ct)
    {
        var access = await directory.ReadProjectAccessAsync(
            actor.OrganizationId,
            actor.UserId,
            plan.ProjectIds,
            ct);
        var visible = access.Where(item => item.Available).ToList();
        if (plan.OwnerUserId != actor.UserId && visible.Count == 0)
        {
            throw new NotFoundException(
                "CAPACITY_PLAN_NOT_FOUND",
                "Capacity plan was not found.");
        }

        var loaded = new List<WorkItemDocument>();
        long sourceCount = 0;
        foreach (var project in visible)
        {
            sourceCount += await workItems.CountByFilterAsync(
                item => item.ProjectId == project.Id
                    && !item.Archived
                    && item.CompletedAt == null
                    && item.Status != "Done",
                ct);
        }
        var truncated = sourceCount > MaximumSourceItems;
        foreach (var project in visible)
        {
            string? cursor = null;
            do
            {
                var remaining = MaximumSourceItems - loaded.Count;
                if (remaining <= 0)
                {
                    break;
                }
                var result = await workItems.ListByCursorAsync(
                    item => item.ProjectId == project.Id
                        && !item.Archived
                        && item.CompletedAt == null
                        && item.Status != "Done",
                    cursor,
                    Math.Min(SourcePageSize, remaining),
                    ct);
                loaded.AddRange(result.Items);
                cursor = result.NextCursor;
            } while (cursor is not null);
            if (loaded.Count == MaximumSourceItems) break;
        }
        return new CapacitySource(
            visible,
            access.Where(item => !item.Available).Select(item => item.Id).ToList(),
            loaded,
            truncated);
    }

    private static CapacityTaskResponse ToTask(WorkItemDocument item) => new(
        item.Id,
        item.ProjectId,
        item.Title,
        item.AssigneeUserId,
        item.DueDate is null ? null : DateOnlyUtc(item.DueDate.Value),
        item.EstimatePoints <= 0 ? null : item.EstimatePoints);

    private static DateOnly DateOnlyUtc(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.UtcDateTime);

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;

    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;

    private static int Percent(decimal allocated, decimal capacity) =>
        capacity <= 0
            ? allocated > 0 ? 100 : 0
            : (int)Math.Round(allocated / capacity * 100m, MidpointRounding.AwayFromZero);

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string State(decimal allocated, decimal capacity)
    {
        if (capacity <= 0)
            return allocated > 0 ? CapacityLoadStates.OverCapacity : CapacityLoadStates.Available;
        var ratio = allocated / capacity;
        return ratio > 1m
            ? CapacityLoadStates.OverCapacity
            : ratio > 0.8m
                ? CapacityLoadStates.NearCapacity
                : CapacityLoadStates.Available;
    }

    private static IReadOnlyCollection<DateOnly> Weeks(DateOnly start, DateOnly end)
    {
        var mondayOffset = ((int)start.DayOfWeek + 6) % 7;
        var current = start.AddDays(-mondayOffset);
        var result = new List<DateOnly>();
        while (current <= end)
        {
            result.Add(current);
            current = current.AddDays(7);
        }
        return result;
    }

    private static int WorkingDays(DateOnly start, DateOnly end)
    {
        if (end < start) return 0;
        var count = 0;
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                count++;
        }
        return count;
    }
}
