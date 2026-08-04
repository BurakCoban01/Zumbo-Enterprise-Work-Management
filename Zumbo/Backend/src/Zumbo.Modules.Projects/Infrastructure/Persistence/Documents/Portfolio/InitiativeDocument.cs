using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class InitiativeDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? ParentInitiativeId { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string Status { get; set; } = InitiativeStatuses.Planned;
    public string Health { get; set; } = InitiativeHealth.NoUpdate;
    public int? Confidence { get; set; }
    public DateTimeOffset? TargetAt { get; set; }
    public List<string> ProjectIds { get; set; } = [];
    public List<PortfolioMilestoneLinkDocument> MilestoneLinks { get; set; } = [];
    public List<InitiativeStatusUpdateDocument> StatusUpdates { get; set; } = [];
}
