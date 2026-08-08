namespace Zumbo.Modules.WorkItems;

public sealed record CompleteChecklistItemCommand(
    string Id,
    string ItemId,
    CompleteChecklistItemRequest Request);
