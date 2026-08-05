namespace Zumbo.Modules.WorkItems;

public sealed record SetWorkItemTeamCommand(
    string Id,
    SetWorkItemTeamRequest Request,
    string CorrelationId);
