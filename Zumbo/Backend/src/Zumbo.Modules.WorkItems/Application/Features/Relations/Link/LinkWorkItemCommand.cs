namespace Zumbo.Modules.WorkItems;

public sealed record LinkWorkItemCommand(
    string Id,
    LinkWorkItemRequest Request,
    string CorrelationId);
