using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class GoalStatusUpdateDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = GoalStatuses.Draft;
    public string Health { get; set; } = GoalHealth.NoUpdate;
    public int? Confidence { get; set; }
    public string Note { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
