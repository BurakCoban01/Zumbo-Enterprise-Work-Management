namespace Zumbo.Modules.Projects;
public sealed record CreateProjectReleaseRequest(
    string VersionId,
    string Name,
    DateTimeOffset? ScheduledAt = null);
