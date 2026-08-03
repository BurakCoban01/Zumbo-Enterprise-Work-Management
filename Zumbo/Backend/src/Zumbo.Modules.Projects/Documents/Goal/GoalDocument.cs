using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class GoalDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset PeriodStartAtUtc { get; set; }
    public DateTimeOffset PeriodEndAtUtc { get; set; }
    public string Status { get; set; } = GoalStatuses.Draft;
    public string Health { get; set; } = GoalHealth.NoUpdate;
    public int? Confidence { get; set; }
    public List<string> ViewerUserIds { get; set; } = [];
    public List<GoalInitiativeLinkDocument> InitiativeLinks { get; set; } = [];
    public List<string> ProjectIds { get; set; } = [];
    public List<KeyResultDocument> KeyResults { get; set; } = [];
    public List<GoalStatusUpdateDocument> StatusUpdates { get; set; } = [];
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
