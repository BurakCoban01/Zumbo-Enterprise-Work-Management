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

public sealed class AutomationRuleVersionDocument
{
    public int Number { get; set; }
    public string State { get; set; } = "Draft";
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AutomationTriggerDocument Trigger { get; set; } = new();
    public AutomationConditionDocument? Condition { get; set; }
    public List<AutomationActionDocument> Actions { get; set; } = [];
    public int MaximumExecutionsPerHour { get; set; }
    public int MaximumChainDepth { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class AutomationTriggerDocument
{
    public string Type { get; set; } = string.Empty;
    public string? EventType { get; set; }
    public int? IntervalMinutes { get; set; }
    public DateTimeOffset? StartAtUtc { get; set; }
}

public sealed class AutomationConditionDocument
{
    public string Kind { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public List<AutomationConditionDocument> Children { get; set; } = [];
}

public sealed class AutomationActionDocument
{
    public string Type { get; set; } = string.Empty;
    public string? Value { get; set; }
}
