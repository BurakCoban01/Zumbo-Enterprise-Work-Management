namespace Zumbo.Modules.WorkItems;

public interface IDevelopmentProjectDirectory
{
    Task<DevelopmentProjectResource> GetAsync(
        string organizationId,
        string projectId,
        CancellationToken ct);
}
