using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class ProjectService
{
    public Task ArchiveAsync(string projectId, CancellationToken ct) => ArchiveAsync(projectId, "none", ct);

    public async Task ArchiveAsync(string projectId, string correlationId, CancellationToken ct)
    {
        var project = await GetProject(projectId, ct);
        EnsureOwner(project);
        var archivedAt = clock.UtcNow;
        project.Archived = true;
        project.ArchivedAt = archivedAt;
        project.RetainUntil = archivedAt.AddDays(lifecycle.ArchiveRetentionDays);
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectArchived", project.Id, "active", "archived", correlationId, ct);
    }

    public async Task<ProjectResponse> RestoreAsync(string projectId, string correlationId, CancellationToken ct)
    {
        var project = await GetArchivedProject(projectId, ct);
        EnsureOwner(project);
        if (project.RetainUntil is not null && project.RetainUntil <= clock.UtcNow)
        {
            throw new ConflictException(
                "PROJECT_RETENTION_EXPIRED",
                "Project retention has expired and the project can no longer be restored.");
        }

        project.Archived = false;
        project.ArchivedAt = null;
        project.RetainUntil = null;
        await SaveAsync(project, ct);
        await audit.WriteAsync("ProjectRestored", project.Id, "archived", "active", correlationId, ct);
        return ToResponse(project);
    }
}
