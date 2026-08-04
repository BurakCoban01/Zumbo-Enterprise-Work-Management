namespace Zumbo.Modules.WorkItems;

public sealed record IntakeRouteAuthorization(
    string OrganizationId,
    string ProjectId,
    string BoardId);
