using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record DefineAutomationRuleRequest(
    string ProjectId,
    string Name,
    string? Description,
    AutomationTriggerRequest Trigger,
    AutomationConditionRequest? Condition,
    IReadOnlyCollection<AutomationActionRequest> Actions,
    int MaximumExecutionsPerHour = 100,
    int MaximumChainDepth = 5);
