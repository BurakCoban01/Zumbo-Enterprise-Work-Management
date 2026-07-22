using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class WorkItemAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Planning_NormalizesValuesAndRaisesDomainEvent()
    {
        var state = NewWorkItem();
        state.SprintId = "old-sprint";
        state.EstimatePoints = 3;
        var aggregate = WorkItemAggregate.Rehydrate(state);

        aggregate.Plan("  sprint-42  ", 8, Now);

        Assert.Equal("sprint-42", state.SprintId);
        Assert.Equal(8, state.EstimatePoints);
        Assert.Equal(Now, state.UpdatedAt);
        var domainEvent = Assert.IsType<WorkItemPlanningChangedDomainEvent>(Assert.Single(aggregate.DomainEvents));
        Assert.Equal("old-sprint", domainEvent.PreviousSprintId);
        Assert.Equal("sprint-42", domainEvent.SprintId);
        Assert.Equal(3, domainEvent.PreviousEstimatePoints);
        Assert.Equal(8, domainEvent.EstimatePoints);

        aggregate.ClearDomainEvents();
        aggregate.Plan("  ", null, Now.AddMinutes(1));

        Assert.Null(state.SprintId);
        Assert.Equal(8, state.EstimatePoints);
        Assert.Single(aggregate.DomainEvents);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1000.01)]
    public void Planning_RejectsEstimateOutsideSupportedRange(decimal estimatePoints)
    {
        var aggregate = WorkItemAggregate.Rehydrate(NewWorkItem());

        var exception = Assert.Throws<ValidationException>(() => aggregate.Plan("sprint", estimatePoints, Now));

        Assert.Equal("VALIDATION_ERROR", exception.Code);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void Transition_PreparationEnforcesStateOwnedRules()
    {
        var state = NewWorkItem();
        var aggregate = WorkItemAggregate.Rehydrate(state);

        var sameStatus = Assert.Throws<ConflictException>(() => aggregate.EnsureCanTarget("to do"));
        Assert.Equal("WORK_ITEM_ALREADY_IN_STATUS", sameStatus.Code);

        state.Checklist.Add(new ChecklistItemDocument { Text = "Review", Completed = false });
        var checklistRule = Rule(requiresCompletedChecklist: true);
        var checklist = Assert.Throws<ConflictException>(() => aggregate.PrepareTransition(checklistRule, Now));
        Assert.Equal("CHECKLIST_INCOMPLETE", checklist.Code);

        state.Checklist[0].Completed = true;
        var assigneeRule = Rule(requiresAssignee: true);
        var assignee = Assert.Throws<ConflictException>(() => aggregate.PrepareTransition(assigneeRule, Now));
        Assert.Equal("ASSIGNEE_REQUIRED", assignee.Code);

        state.AssigneeUserId = "user-1";
        var approvalRule = Rule(requiresApproval: true);
        var approval = Assert.Throws<ConflictException>(() => aggregate.PrepareTransition(approvalRule, Now));
        Assert.Equal("WORK_ITEM_APPROVAL_REQUIRED", approval.Code);
    }

    [Fact]
    public void MoveAppliesTransitionAsOneAggregateOperation()
    {
        var approved = new WorkItemApprovalDocument
        {
            Id = "approval-1",
            FromStatus = "To Do",
            ToStatus = "Done",
            Status = "Approved",
            DecidedAt = Now.AddMinutes(-2),
            ExpiresAt = Now.AddHours(1)
        };
        var stale = new WorkItemApprovalDocument
        {
            Id = "approval-2",
            FromStatus = "To Do",
            ToStatus = "Review",
            Status = "Pending",
            ExpiresAt = Now.AddHours(1)
        };
        var state = NewWorkItem();
        state.Approvals.AddRange([approved, stale]);
        state.Labels.Add("obsolete");
        var aggregate = WorkItemAggregate.Rehydrate(state);
        var rule = Rule(
            requiresApproval: true,
            automations:
            [
                new WorkflowAutomationRule("AssignToActor", null),
                new WorkflowAutomationRule("AddLabel", "released"),
                new WorkflowAutomationRule("RemoveLabel", "obsolete")
            ]);
        var prepared = aggregate.PrepareTransition(rule, Now);

        aggregate.Move(rule, new BoardPlacement("done-column", "Done", true), 4096, prepared, Now, "actor-7");

        Assert.Equal("Done", state.Status);
        Assert.Equal("done-column", state.ColumnId);
        Assert.Equal(4096, state.Rank);
        Assert.Equal(Now, state.CompletedAt);
        Assert.Equal("actor-7", state.AssigneeUserId);
        Assert.Contains("released", state.Labels);
        Assert.DoesNotContain("obsolete", state.Labels);
        Assert.Equal(Now, approved.ConsumedAt);
        Assert.Equal("Cancelled", stale.Status);
        var history = Assert.Single(state.StatusHistory);
        Assert.Equal("To Do", history.FromStatus);
        Assert.Equal("Done", history.ToStatus);
        Assert.Equal("actor-7", history.ChangedByUserId);
        var domainEvent = Assert.IsType<WorkItemMovedDomainEvent>(Assert.Single(aggregate.DomainEvents));
        Assert.Equal(state.Id, domainEvent.WorkItemId);
        Assert.Equal("To Do", domainEvent.FromStatus);
        Assert.Equal("Done", domainEvent.ToStatus);
    }

    [Fact]
    public void DomainEventMapperCreatesVersionedProviderNeutralContracts()
    {
        var mapper = new WorkItemDomainEventMapper();
        var planning = mapper.Map(new WorkItemPlanningChangedDomainEvent("wi-1", null, "s-1", 0, 5, Now));
        var moved = mapper.Map(new WorkItemMovedDomainEvent("wi-1", "p-1", "b-1", "To Do", "Done", Now));

        Assert.Equal("work-item.planning-changed.v1", planning.EventName);
        Assert.Equal("work-item.moved.v1", moved.EventName);
        Assert.Equal("wi-1", planning.AggregateId);
        Assert.Equal("wi-1", moved.AggregateId);
        Assert.False(string.IsNullOrWhiteSpace(planning.EventId));
        Assert.False(string.IsNullOrWhiteSpace(moved.EventId));
        Assert.Equal(Now, planning.OccurredAt);
        Assert.Equal(Now, moved.OccurredAt);
    }

    private static WorkItemDocument NewWorkItem() => new()
    {
        Id = "wi-1",
        ProjectId = "project-1",
        BoardId = "board-1",
        ColumnId = "todo-column",
        Status = "To Do",
        Title = "Aggregate test"
    };

    private static WorkflowTransitionRule Rule(
        bool requiresAssignee = false,
        bool requiresCompletedChecklist = false,
        bool requiresApproval = false,
        IReadOnlyCollection<WorkflowAutomationRule>? automations = null) =>
        new(
            "To Do",
            "Done",
            requiresAssignee,
            requiresCompletedChecklist,
            requiresApproval,
            automations,
            "Done");
}
