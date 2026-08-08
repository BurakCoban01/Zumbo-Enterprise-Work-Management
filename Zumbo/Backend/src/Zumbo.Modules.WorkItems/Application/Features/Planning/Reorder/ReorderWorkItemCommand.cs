namespace Zumbo.Modules.WorkItems;

public sealed record ReorderWorkItemCommand(
    string Id,
    ReorderWorkItemRequest Request,
    string CorrelationId);
