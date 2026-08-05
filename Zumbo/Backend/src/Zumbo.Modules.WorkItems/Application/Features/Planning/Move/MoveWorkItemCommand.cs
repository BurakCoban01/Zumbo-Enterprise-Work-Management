namespace Zumbo.Modules.WorkItems;

public sealed record MoveWorkItemCommand(
    string Id,
    MoveWorkItemRequest Request,
    string CorrelationId);
