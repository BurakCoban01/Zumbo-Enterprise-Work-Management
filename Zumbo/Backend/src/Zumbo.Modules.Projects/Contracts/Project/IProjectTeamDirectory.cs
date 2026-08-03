namespace Zumbo.Modules.Projects;

public interface IProjectTeamDirectory
{
    Task<ProjectTeamDirectoryEntry?> FindAsync(string teamId, CancellationToken ct);
}
