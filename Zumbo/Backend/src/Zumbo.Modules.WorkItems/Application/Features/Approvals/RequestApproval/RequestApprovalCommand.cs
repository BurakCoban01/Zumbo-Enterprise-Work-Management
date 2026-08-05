namespace Zumbo.Modules.WorkItems;

public sealed record RequestApprovalCommand(
    string Id,
    RequestWorkItemApprovalRequest Request,
    string CorrelationId);
