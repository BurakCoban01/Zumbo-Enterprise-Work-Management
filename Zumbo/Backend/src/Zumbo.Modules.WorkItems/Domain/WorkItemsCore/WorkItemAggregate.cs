using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemAggregate : AggregateRoot
{
    private readonly WorkItemDocument _state;

    private WorkItemAggregate(WorkItemDocument state)
    {
        _state = state;
        Id = state.Id;
    }

    public string Status => _state.Status;

    public SprintAssignment Planning => new(_state.SprintId, _state.EstimatePoints);

    public static WorkItemAggregate Rehydrate(WorkItemDocument state) => new(state);

    public void EnsureCanTarget(string targetStatus)
    {
        if (_state.Status.Equals(targetStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("WORK_ITEM_ALREADY_IN_STATUS", "Work item is already in the requested status.");
        }
    }

    public PreparedWorkItemTransition PrepareTransition(WorkflowTransitionRule rule, DateTimeOffset now)
    {
        if (rule.RequiresCompletedChecklist && _state.Checklist.Any(item => !item.Completed))
        {
            throw new ConflictException("CHECKLIST_INCOMPLETE", "All checklist items must be completed before moving to Done.");
        }

        if (rule.RequiresAssignee && string.IsNullOrWhiteSpace(_state.AssigneeUserId))
        {
            throw new ConflictException("ASSIGNEE_REQUIRED", "Assignee is required for this transition.");
        }

        if (!rule.RequiresApproval)
        {
            return new PreparedWorkItemTransition(null);
        }

        var approval = _state.Approvals
            .Where(item => item.Status == "Approved" && item.ConsumedAt is null && item.ExpiresAt > now)
            .Where(item => item.FromStatus.Equals(_state.Status, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.ToStatus.Equals(rule.ToStatus, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.DecidedAt)
            .FirstOrDefault()
            ?? throw new ConflictException("WORK_ITEM_APPROVAL_REQUIRED", "An approved transition request is required.");

        return new PreparedWorkItemTransition(approval.Id);
    }

    public void Move(
        WorkflowTransitionRule rule,
        BoardPlacement placement,
        long rank,
        PreparedWorkItemTransition preparedTransition,
        DateTimeOffset now,
        string actorUserId)
    {
        var oldStatus = _state.Status;
        _state.Status = placement.Status;
        _state.ColumnId = placement.ColumnId;
        _state.Rank = rank;
        _state.CompletedAt = rule.ToStatusCategory.Equals("Done", StringComparison.OrdinalIgnoreCase) ? now : null;
        ApplyAutomations(rule.Automations, actorUserId);

        if (preparedTransition.ApprovalId is not null)
        {
            var consumedApproval = _state.Approvals.Single(item => item.Id == preparedTransition.ApprovalId);
            consumedApproval.ConsumedAt = now;
        }

        foreach (var staleApproval in _state.Approvals.Where(item =>
            item.Id != preparedTransition.ApprovalId
            && item.ConsumedAt is null
            && item.FromStatus.Equals(oldStatus, StringComparison.OrdinalIgnoreCase)
            && item.Status is "Pending" or "Approved"))
        {
            staleApproval.Status = "Cancelled";
        }

        _state.StatusHistory.Add(new WorkItemStatusHistoryDocument
        {
            FromStatus = oldStatus,
            ToStatus = placement.Status,
            ChangedByUserId = actorUserId,
            ChangedAt = now
        });
        _state.UpdatedAt = now;
        Raise(new WorkItemMovedDomainEvent(
            _state.Id,
            _state.ProjectId,
            _state.BoardId,
            oldStatus,
            placement.Status,
            now));
    }

    public void Plan(string? sprintId, decimal? estimatePoints, DateTimeOffset now)
    {
        var previous = Planning;
        var next = SprintAssignment.Create(sprintId, estimatePoints, previous.EstimatePoints);
        _state.SprintId = next.SprintId;
        _state.EstimatePoints = next.EstimatePoints;
        _state.UpdatedAt = now;
        Raise(new WorkItemPlanningChangedDomainEvent(
            _state.Id,
            previous.SprintId,
            next.SprintId,
            previous.EstimatePoints,
            next.EstimatePoints,
            now));
    }

    private void ApplyAutomations(
        IReadOnlyCollection<WorkflowAutomationRule>? automations,
        string actorUserId)
    {
        foreach (var automation in automations ?? [])
        {
            switch (automation.Action)
            {
                case "AssignToActor":
                    _state.AssigneeUserId = actorUserId;
                    break;
                case "ClearAssignee":
                    _state.AssigneeUserId = null;
                    break;
                case "AddLabel" when automation.Value is not null:
                    if (!_state.Labels.Contains(automation.Value, StringComparer.OrdinalIgnoreCase))
                    {
                        _state.Labels.Add(automation.Value);
                    }
                    break;
                case "RemoveLabel" when automation.Value is not null:
                    _state.Labels.RemoveAll(label => label.Equals(automation.Value, StringComparison.OrdinalIgnoreCase));
                    break;
                default:
                    throw new ConflictException("WORKFLOW_AUTOMATION_INVALID", "Workflow contains an unsupported automation.");
            }
        }
    }
}
