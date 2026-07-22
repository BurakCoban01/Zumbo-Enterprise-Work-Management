using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class SprintAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_CapturesCommittedScopeAndActivatesSprint()
    {
        var sprint = PlannedSprint();

        SprintAggregate.Rehydrate(sprint).Start(3, 13m, Now);

        Assert.Equal(SprintStatuses.Active, sprint.Status);
        Assert.Equal(3, sprint.CommittedItems);
        Assert.Equal(13m, sprint.CommittedPoints);
        Assert.Equal(Now, sprint.StartedAt);
    }

    [Fact]
    public void Start_WithoutScopeOrFromNonPlannedState_IsRejected()
    {
        var empty = Assert.Throws<ConflictException>(() =>
            SprintAggregate.Rehydrate(PlannedSprint()).Start(0, 0, Now));
        var active = PlannedSprint();
        active.Status = SprintStatuses.Active;
        var invalidState = Assert.Throws<ConflictException>(() =>
            SprintAggregate.Rehydrate(active).Start(1, 3, Now));

        Assert.Equal("SPRINT_SCOPE_EMPTY", empty.Code);
        Assert.Equal("SPRINT_START_INVALID_STATE", invalidState.Code);
    }

    [Fact]
    public void Complete_FreezesOutcomeAndRejectsRepeatedCompletion()
    {
        var sprint = PlannedSprint();
        SprintAggregate.Rehydrate(sprint).Start(3, 13m, Now);

        SprintAggregate.Rehydrate(sprint).Complete(2, 8m, 1, 5m, Now.AddDays(5));

        Assert.Equal(SprintStatuses.Completed, sprint.Status);
        Assert.Equal(2, sprint.CompletedItems);
        Assert.Equal(8m, sprint.CompletedPoints);
        Assert.Equal(1, sprint.CarryoverItems);
        Assert.Equal(5m, sprint.CarryoverPoints);
        var repeated = Assert.Throws<ConflictException>(() =>
            SprintAggregate.Rehydrate(sprint).Complete(3, 13m, 0, 0, Now.AddDays(6)));
        Assert.Equal("SPRINT_COMPLETE_INVALID_STATE", repeated.Code);
    }

    private static SprintDocument PlannedSprint() => new()
    {
        Id = "sprint-1",
        ProjectId = "project-1",
        Name = "Sprint 1",
        StartAtUtc = Now,
        EndAtUtc = Now.AddDays(13),
        CreatedAt = Now,
        UpdatedAt = Now
    };
}
