namespace Zumbo.Modules.Projects;

public sealed record ProjectReleaseResponse(
    string Id,
    string VersionId,
    string Name,
    string Status,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PublishedAt);
