namespace Zumbo.Modules.WorkItems;

public sealed record SetCustomFieldsCommand(
    string Id,
    SetWorkItemCustomFieldsRequest Request,
    string CorrelationId);
