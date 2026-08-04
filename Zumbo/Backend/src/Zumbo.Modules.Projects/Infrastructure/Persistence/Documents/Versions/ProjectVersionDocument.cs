using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class ProjectVersionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = ProjectVersionStatuses.Planned;
    public DateTimeOffset? ReleasedAt { get; set; }
}
