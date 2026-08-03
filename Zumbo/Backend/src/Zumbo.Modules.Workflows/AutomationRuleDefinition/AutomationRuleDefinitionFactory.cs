using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public static class AutomationRuleDefinitionFactory
{
    public const int MaximumConditionDepth = 4;
    public const int MaximumConditionNodes = 50;
    public const int MaximumActions = 10;

    private static readonly HashSet<string> EventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "WorkItemCreated",
        "WorkItemUpdated",
        "WorkItemTransitioned"
    };

    private static readonly HashSet<string> ConditionFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Status",
        "PreviousStatus",
        "Priority",
        "Type",
        "AssigneeUserId",
        "Labels"
    };

    public static AutomationRuleDefinition Define(DefineAutomationRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            throw new ValidationException("Automation project is required.");

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            throw new ValidationException("Automation name must contain between 1 and 120 characters.");

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description?.Length > 1000)
            throw new ValidationException("Automation description cannot exceed 1000 characters.");
        if (request.Actions is null || request.Actions.Count is 0 or > MaximumActions)
            throw new ValidationException($"Automation must contain between 1 and {MaximumActions} actions.");
        if (request.MaximumExecutionsPerHour is < 1 or > 1000)
            throw new ValidationException("Automation hourly execution limit must be between 1 and 1000.");
        if (request.MaximumChainDepth is < 1 or > 10)
            throw new ValidationException("Automation chain depth must be between 1 and 10.");

        var nodeCount = 0;
        var condition = request.Condition is null
            ? null
            : NormalizeCondition(request.Condition, 1, ref nodeCount);

        return new AutomationRuleDefinition(
            request.ProjectId.Trim(),
            name,
            description,
            NormalizeTrigger(request.Trigger),
            condition,
            request.Actions.Select(NormalizeAction).ToArray(),
            request.MaximumExecutionsPerHour,
            request.MaximumChainDepth);
    }

    private static AutomationTriggerDefinition NormalizeTrigger(AutomationTriggerRequest? trigger)
    {
        if (trigger is null)
            throw new ValidationException("Automation trigger is required.");

        var type = trigger.Type?.Trim().ToLowerInvariant() switch
        {
            "event" => "Event",
            "schedule" => "Schedule",
            _ => throw new ValidationException("Automation trigger type must be Event or Schedule.")
        };

        if (type == "Event")
        {
            var eventType = trigger.EventType?.Trim();
            eventType = EventTypes.SingleOrDefault(candidate =>
                candidate.Equals(eventType, StringComparison.OrdinalIgnoreCase));
            if (eventType is null)
                throw new ValidationException("Automation event trigger is not supported.");
            if (trigger.IntervalMinutes is not null || trigger.StartAtUtc is not null)
                throw new ValidationException("Event triggers cannot include schedule settings.");
            return new AutomationTriggerDefinition(type, eventType, null, null);
        }

        if (trigger.IntervalMinutes is not { } intervalMinutes
            || intervalMinutes is < 5 or > 525_600)
            throw new ValidationException("Automation schedule interval must be between 5 and 525600 minutes.");
        if (!string.IsNullOrWhiteSpace(trigger.EventType))
            throw new ValidationException("Schedule triggers cannot include an event type.");
        return new AutomationTriggerDefinition(
            type,
            null,
            intervalMinutes,
            trigger.StartAtUtc?.ToUniversalTime());
    }

    private static AutomationConditionDefinition NormalizeCondition(
        AutomationConditionRequest condition,
        int depth,
        ref int nodeCount)
    {
        nodeCount++;
        if (nodeCount > MaximumConditionNodes)
            throw new ValidationException($"Automation condition tree cannot exceed {MaximumConditionNodes} nodes.");
        if (depth > MaximumConditionDepth)
            throw new ValidationException($"Automation condition tree cannot exceed {MaximumConditionDepth} levels.");

        var kind = condition.Kind?.Trim().ToLowerInvariant() switch
        {
            "all" => "All",
            "any" => "Any",
            "field" => "Field",
            _ => throw new ValidationException("Automation condition kind must be All, Any or Field.")
        };
        var children = condition.Children?.ToArray() ?? [];

        if (kind is "All" or "Any")
        {
            if (children.Length is 0 or > 20)
                throw new ValidationException("Automation condition groups must contain between 1 and 20 children.");
            if (!string.IsNullOrWhiteSpace(condition.Field)
                || !string.IsNullOrWhiteSpace(condition.Operator)
                || condition.Value is not null)
                throw new ValidationException("Automation condition groups cannot contain field comparison values.");
            var normalizedChildren = new List<AutomationConditionDefinition>(children.Length);
            foreach (var child in children)
                normalizedChildren.Add(NormalizeCondition(child, depth + 1, ref nodeCount));
            return new AutomationConditionDefinition(kind, null, null, null, normalizedChildren);
        }

        if (children.Length > 0)
            throw new ValidationException("Automation field conditions cannot contain child conditions.");
        var field = ConditionFields.SingleOrDefault(candidate =>
            candidate.Equals(condition.Field?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException("Automation condition field is not supported.");
        var comparison = condition.Operator?.Trim().ToLowerInvariant() switch
        {
            "equals" or "eq" => "Equals",
            "not-equals" or "neq" => "NotEquals",
            "contains" => "Contains",
            "not-contains" => "NotContains",
            "is-empty" => "IsEmpty",
            "is-not-empty" => "IsNotEmpty",
            _ => throw new ValidationException("Automation condition operator is not supported.")
        };
        var value = string.IsNullOrWhiteSpace(condition.Value) ? null : condition.Value.Trim();
        if (comparison is "IsEmpty" or "IsNotEmpty")
        {
            if (value is not null)
                throw new ValidationException("Empty-state automation conditions cannot include a value.");
        }
        else if (value is null || value.Length > 200)
        {
            throw new ValidationException("Automation field comparison requires a value of at most 200 characters.");
        }

        return new AutomationConditionDefinition("Field", field, comparison, value, []);
    }

    private static AutomationActionDefinition NormalizeAction(AutomationActionRequest action)
    {
        var type = action.Type?.Trim().ToLowerInvariant() switch
        {
            "assign-to-actor" or "assigntoactor" => "AssignToActor",
            "assign-user" or "assignuser" => "AssignUser",
            "clear-assignee" or "clearassignee" => "ClearAssignee",
            "add-label" or "addlabel" => "AddLabel",
            "remove-label" or "removelabel" => "RemoveLabel",
            "set-priority" or "setpriority" => "SetPriority",
            "add-comment" or "addcomment" => "AddComment",
            _ => throw new ValidationException("Automation action type is not supported.")
        };
        var value = string.IsNullOrWhiteSpace(action.Value) ? null : action.Value.Trim();

        switch (type)
        {
            case "AssignToActor":
            case "ClearAssignee":
                if (value is not null)
                    throw new ValidationException($"{type} automation actions do not accept a value.");
                break;
            case "AddLabel":
            case "RemoveLabel":
                if (value is null || value.Length > 50)
                    throw new ValidationException("Label automation actions require a value of at most 50 characters.");
                break;
            case "AssignUser":
                if (value is null || value.Length > 128)
                    throw new ValidationException("AssignUser automation actions require a user id of at most 128 characters.");
                break;
            case "SetPriority":
                value = value?.ToLowerInvariant() switch
                {
                    "critical" => "Critical",
                    "high" => "High",
                    "medium" => "Medium",
                    "low" => "Low",
                    _ => throw new ValidationException("SetPriority automation actions require a supported priority.")
                };
                break;
            case "AddComment":
                if (value is null || value.Length > 2000)
                    throw new ValidationException("AddComment automation actions require text of at most 2000 characters.");
                break;
        }

        return new AutomationActionDefinition(type, value);
    }
}
