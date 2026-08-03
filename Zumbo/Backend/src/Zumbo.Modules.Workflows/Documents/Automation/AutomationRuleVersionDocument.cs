using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

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
