using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class ProjectReleaseDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string VersionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = ProjectReleaseStatuses.Draft;
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
