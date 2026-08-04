using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public sealed class AutomationRuleDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public AutomationRuleVersionDocument? Draft { get; set; }
    public List<AutomationRuleVersionDocument> PublishedVersions { get; set; } = [];
    public int PublishedVersion { get; set; }
    public bool Active { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset? NextRunAtUtc { get; set; }
    public DateTimeOffset? ScheduleClaimedForUtc { get; set; }
    public DateTimeOffset? ScheduleClaimedUntilUtc { get; set; }
    public string? ScheduleClaimToken { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
