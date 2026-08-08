namespace Zumbo.Modules.WorkItems;

public sealed record UnlinkWorkItemCommand(
    string Id,
    string RelatedWorkItemId,
    string RelationType,
    string CorrelationId);
