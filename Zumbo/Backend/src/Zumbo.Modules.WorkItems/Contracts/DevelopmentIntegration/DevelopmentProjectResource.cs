namespace Zumbo.Modules.WorkItems;

public sealed record DevelopmentProjectResource(
    string OrganizationId,
    string ProjectId,
    string ProjectKey,
    string ProjectName);
