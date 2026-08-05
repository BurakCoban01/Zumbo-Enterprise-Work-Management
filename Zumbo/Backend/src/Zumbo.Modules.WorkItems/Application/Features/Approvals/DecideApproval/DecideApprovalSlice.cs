using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class DecideApprovalSlice(ApprovalMutationPipeline pipeline)
{
    internal async Task<WorkItemResponse> HandleAsync(
        DecideApprovalCommand command,
        CancellationToken ct)
    {
        var workItem = await pipeline.LoadForDecisionAsync(command.Id, ct);
        var approval = workItem.Approvals.SingleOrDefault(item => item.Id == command.ApprovalId)
            ?? throw new NotFoundException(
                "WORK_ITEM_APPROVAL_NOT_FOUND",
                "Work item approval was not found.");
        if (approval.Status != "Pending")
        {
            throw new ConflictException(
                "WORK_ITEM_APPROVAL_DECIDED",
                "Work item approval has already been decided.");
        }

        var now = pipeline.UtcNow;
        if (approval.ExpiresAt <= now)
        {
            approval.Status = "Expired";
            workItem.UpdatedAt = now;
            await pipeline.PersistExpirationAsync(
                workItem,
                approval,
                command.CorrelationId,
                ct);
            throw new ConflictException(
                "WORK_ITEM_APPROVAL_EXPIRED",
                "Work item approval has expired.");
        }

        var actorUserId = pipeline.CurrentUserId;
        if (approval.RequestedByUserId == actorUserId)
        {
            throw new ForbiddenException("Approval requester cannot decide their own request.");
        }

        var note = command.Request.Note?.Trim();
        if (note?.Length > 1000)
        {
            throw new ValidationException("Approval note cannot exceed 1000 characters.");
        }

        approval.Status = command.Request.Approved ? "Approved" : "Rejected";
        approval.DecidedByUserId = actorUserId;
        approval.DecidedAt = now;
        approval.Note = string.IsNullOrWhiteSpace(note) ? null : note;
        workItem.UpdatedAt = now;
        return await pipeline.PersistDecisionAsync(
            workItem,
            approval,
            command.CorrelationId,
            ct);
    }
}
