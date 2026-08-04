using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class ProjectMilestoneDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public string Status { get; set; } = ProjectMilestoneStatuses.Open;
    public DateTimeOffset? CompletedAt { get; set; }
}
