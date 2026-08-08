namespace Zumbo.Modules.WorkItems;

public sealed record DecideApprovalCommand(
    string Id,
    string ApprovalId,
    DecideWorkItemApprovalRequest Request,
    string CorrelationId);
