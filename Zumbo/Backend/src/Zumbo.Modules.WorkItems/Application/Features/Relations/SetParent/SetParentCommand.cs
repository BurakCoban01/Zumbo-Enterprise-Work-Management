namespace Zumbo.Modules.WorkItems;

public sealed record SetParentCommand(
    string Id,
    SetWorkItemParentRequest Request,
    string CorrelationId);
