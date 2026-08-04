namespace Zumbo.Modules.Projects;

public interface IProjectMemberDirectory
{
    Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct);
}
