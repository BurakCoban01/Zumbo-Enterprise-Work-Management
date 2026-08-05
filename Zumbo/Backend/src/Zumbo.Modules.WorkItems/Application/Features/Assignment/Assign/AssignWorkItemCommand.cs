namespace Zumbo.Modules.WorkItems;

public sealed record AssignWorkItemCommand(
    string Id,
    AssignWorkItemRequest Request,
    string CorrelationId);
