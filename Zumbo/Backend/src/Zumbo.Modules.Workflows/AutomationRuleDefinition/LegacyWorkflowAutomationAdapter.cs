using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public static class LegacyWorkflowAutomationAdapter
{
    public static IReadOnlyCollection<AutomationActionDefinition> ToTypedActions(
        IReadOnlyCollection<WorkflowAutomationRequest>? automations) =>
        (automations ?? [])
            .Select(automation => AutomationRuleDefinitionFactory.Define(new DefineAutomationRuleRequest(
                "legacy-workflow",
                "Legacy workflow transition",
                null,
                new AutomationTriggerRequest("Event", "WorkItemTransitioned"),
                null,
                [new AutomationActionRequest(automation.Action, automation.Value)])))
            .SelectMany(rule => rule.Actions)
            .ToArray();
}
