using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class AutomationRuleDefinitionTests
{
    [Fact]
    public void Define_NormalizesVersionableTriggerConditionAndTypedActions()
    {
        var rule = AutomationRuleDefinitionFactory.Define(new DefineAutomationRuleRequest(
            " project-1 ",
            " Escalate urgent work ",
            " Route unassigned urgent work ",
            new AutomationTriggerRequest(" event ", " workitemtransitioned "),
            new AutomationConditionRequest("all", Children:
            [
                new("field", "priority", "equals", "high"),
                new("any", Children:
                [
                    new("field", "AssigneeUserId", "is-empty"),
                    new("field", "Labels", "contains", "triage")
                ])
            ]),
            [
                new("set-priority", "critical"),
                new("add-label", "escalated"),
                new("assign-to-actor")
            ],
            MaximumExecutionsPerHour: 40,
            MaximumChainDepth: 3));

        Assert.Equal("project-1", rule.ProjectId);
        Assert.Equal("Escalate urgent work", rule.Name);
        Assert.Equal("Event", rule.Trigger.Type);
        Assert.Equal("WorkItemTransitioned", rule.Trigger.EventType);
        Assert.Equal("All", rule.Condition!.Kind);
        Assert.Equal("Any", rule.Condition.Children.ElementAt(1).Kind);
        Assert.Equal(["SetPriority", "AddLabel", "AssignToActor"], rule.Actions.Select(x => x.Type));
        Assert.Equal("Critical", rule.Actions.First().Value);
        Assert.Equal(40, rule.MaximumExecutionsPerHour);
        Assert.Equal(3, rule.MaximumChainDepth);
    }

    [Fact]
    public void Define_ScheduleRejectsEventSettingsAndUnboundedInterval()
    {
        var invalidEvent = Assert.Throws<ValidationException>(() =>
            AutomationRuleDefinitionFactory.Define(BasicRequest(
                new AutomationTriggerRequest("Schedule", "WorkItemCreated", 15))));
        var invalidInterval = Assert.Throws<ValidationException>(() =>
            AutomationRuleDefinitionFactory.Define(BasicRequest(
                new AutomationTriggerRequest("Schedule", IntervalMinutes: 1))));

        Assert.Equal("Schedule triggers cannot include an event type.", invalidEvent.Message);
        Assert.Equal(
            "Automation schedule interval must be between 5 and 525600 minutes.",
            invalidInterval.Message);
    }

    [Fact]
    public void Define_RejectsConditionTreeBeyondBoundedDepth()
    {
        var condition = new AutomationConditionRequest(
            "All",
            Children:
            [
                new("All", Children:
                [
                    new("All", Children:
                    [
                        new("All", Children:
                        [
                            new("Field", "Status", "Equals", "Done")
                        ])
                    ])
                ])
            ]);

        var error = Assert.Throws<ValidationException>(() =>
            AutomationRuleDefinitionFactory.Define(BasicRequest(
                new AutomationTriggerRequest("Event", "WorkItemUpdated"),
                condition)));

        Assert.Equal("Automation condition tree cannot exceed 4 levels.", error.Message);
    }

    [Fact]
    public void LegacyAdapter_PreservesAllSupportedTransitionActions()
    {
        var actions = LegacyWorkflowAutomationAdapter.ToTypedActions(
        [
            new("AssignToActor"),
            new("ClearAssignee"),
            new("AddLabel", "released"),
            new("RemoveLabel", "blocked")
        ]);

        Assert.Collection(
            actions,
            action => Assert.Equal(new AutomationActionDefinition("AssignToActor", null), action),
            action => Assert.Equal(new AutomationActionDefinition("ClearAssignee", null), action),
            action => Assert.Equal(new AutomationActionDefinition("AddLabel", "released"), action),
            action => Assert.Equal(new AutomationActionDefinition("RemoveLabel", "blocked"), action));
    }

    private static DefineAutomationRuleRequest BasicRequest(
        AutomationTriggerRequest trigger,
        AutomationConditionRequest? condition = null) =>
        new(
            "project-1",
            "Automation",
            null,
            trigger,
            condition,
            [new AutomationActionRequest("AddLabel", "automated")]);
}
