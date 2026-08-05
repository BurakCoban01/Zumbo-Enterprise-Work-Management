namespace Zumbo.Modules.WorkItems;

public sealed record UpdateWorkItemCommand(
    string Id,
    UpdateWorkItemRequest Request,
    string CorrelationId);
