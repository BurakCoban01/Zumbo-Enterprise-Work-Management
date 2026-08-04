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
}
