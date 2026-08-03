namespace Zumbo.Modules.Projects;

public interface IProjectOrganizationDirectory
{
    Task EnsureActiveAsync(string organizationId, CancellationToken ct);
}
