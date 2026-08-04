namespace Zumbo.Modules.Projects;

public interface IProjectTeamUsageChecker
{
    Task<bool> HasWorkItemsAsync(string projectId, string teamId, CancellationToken ct);
}
