namespace Zumbo.Modules.Projects;

internal sealed class AllowActiveProjectOrganizationDirectory : IProjectOrganizationDirectory
{
    internal static readonly AllowActiveProjectOrganizationDirectory Instance = new();
    public Task EnsureActiveAsync(string organizationId, CancellationToken ct) => Task.CompletedTask;
}
