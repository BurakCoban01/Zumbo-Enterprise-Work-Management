using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationRuleDefinition(
    string ProjectId,
    string Name,
    string? Description,
    AutomationTriggerDefinition Trigger,
    AutomationConditionDefinition? Condition,
    IReadOnlyCollection<AutomationActionDefinition> Actions,
    int MaximumExecutionsPerHour,
    int MaximumChainDepth);
