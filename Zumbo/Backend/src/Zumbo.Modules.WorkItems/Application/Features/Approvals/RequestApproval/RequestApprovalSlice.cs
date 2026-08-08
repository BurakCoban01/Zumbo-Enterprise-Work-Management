using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class RequestApprovalSlice(
    ApprovalMutationPipeline pipeline,
    IWorkflowPolicy workflowPolicy)
{
    internal async Task<WorkItemResponse> HandleAsync(
        RequestApprovalCommand command,
        CancellationToken ct)
    {
        var workItem = await pipeline.LoadForRequestAsync(command.Id, ct);
        var target = command.Request.TargetStatus?.Trim();
        if (string.IsNullOrWhiteSpace(target)
            || workItem.Status.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                "Approval target status must differ from the current status.");
        }

        var rule = await workflowPolicy.EnsureTransitionAllowedAsync(
            workItem.ProjectId,
            workItem.Type,
            workItem.Status,
            target,
            ct);
        if (!rule.RequiresApproval)
        {
            throw new ConflictException(
                "WORK_ITEM_APPROVAL_NOT_REQUIRED",
                "The requested transition does not require approval.");
        }

        var now = pipeline.UtcNow;
        if (workItem.Approvals.Any(approval =>
            approval.FromStatus.Equals(workItem.Status, StringComparison.OrdinalIgnoreCase)
            && approval.ToStatus.Equals(target, StringComparison.OrdinalIgnoreCase)
            && approval.ConsumedAt is null
            && approval.ExpiresAt > now
            && approval.Status is "Pending" or "Approved"))
        {
            throw new ConflictException(
                "WORK_ITEM_APPROVAL_EXISTS",
                "An active approval already exists for this transition.");
        }

        var approval = new WorkItemApprovalDocument
        {
            FromStatus = workItem.Status,
            ToStatus = rule.ToStatus,
            RequestedByUserId = pipeline.CurrentUserId,
            RequestedAt = now,
            ExpiresAt = now.AddDays(7)
        };
        workItem.Approvals.Add(approval);
        workItem.UpdatedAt = now;
        return await pipeline.PersistRequestAsync(
            workItem,
            approval,
            command.CorrelationId,
            ct);
    }
}
