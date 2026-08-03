using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class WorkflowDefinitionAggregate : AggregateRoot
{
    private WorkflowDefinitionAggregate(
        string projectId,
        IReadOnlyCollection<WorkflowStatusRequest> statuses,
        IReadOnlyCollection<WorkflowTransitionRequest> transitions,
        DateTimeOffset definedAt)
    {
        Id = projectId;
        ProjectId = projectId;
        Statuses = statuses;
        Transitions = transitions;
        DefinedAt = definedAt;
    }

    public string ProjectId { get; }
    public IReadOnlyCollection<WorkflowStatusRequest> Statuses { get; }
    public IReadOnlyCollection<WorkflowTransitionRequest> Transitions { get; }
    public DateTimeOffset DefinedAt { get; }

    public static WorkflowDefinitionAggregate Define(
        string projectId,
        IReadOnlyCollection<WorkflowStatusRequest>? statuses,
        IReadOnlyCollection<WorkflowTransitionRequest> transitions,
        DateTimeOffset occurredAt)
    {
        var preparedStatuses = NormalizeStatuses(statuses, transitions);
        ValidateWorkflow(preparedStatuses, transitions);

        var normalizedStatuses = preparedStatuses.Select(status => new WorkflowStatusRequest(
            status.Name.Trim(),
            NormalizeCategory(status.Category))).ToArray();
        var normalizedTransitions = transitions.Select(transition => new WorkflowTransitionRequest(
            transition.FromStatus.Trim(),
            transition.ToStatus.Trim(),
            transition.RequiresAssignee,
            transition.RequiresCompletedChecklist,
            transition.RequiresApproval,
            NormalizeAutomations(transition.Automations))).ToArray();

        var aggregate = new WorkflowDefinitionAggregate(
            projectId,
            normalizedStatuses,
            normalizedTransitions,
            occurredAt);
        aggregate.Raise(new WorkflowDefinedDomainEvent(
            aggregate.Id,
            projectId,
            normalizedStatuses.Length,
            normalizedTransitions.Length,
            occurredAt));
        return aggregate;
    }

    private static void ValidateWorkflow(
        IReadOnlyCollection<WorkflowStatusRequest> statuses,
        IReadOnlyCollection<WorkflowTransitionRequest> transitions)
    {
        if (transitions.Count > 200)
        {
            throw new ValidationException("A workflow cannot contain more than 200 transitions.");
        }

        if (transitions.Any(x => string.IsNullOrWhiteSpace(x.FromStatus) || string.IsNullOrWhiteSpace(x.ToStatus)))
        {
            throw new ValidationException("Workflow transition statuses are required.");
        }

        if (transitions.Any(x => x.FromStatus.Trim().Equals(x.ToStatus.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException("A workflow transition cannot target the same status.");
        }

        var duplicate = transitions
            .GroupBy(x => $"{x.FromStatus.Trim().ToLowerInvariant()}->{x.ToStatus.Trim().ToLowerInvariant()}")
            .Any(x => x.Count() > 1);
        if (duplicate)
        {
            throw new ConflictException("WORKFLOW_TRANSITION_DUPLICATE", "Workflow transitions must be unique.");
        }

        foreach (var transition in transitions)
        {
            _ = NormalizeAutomations(transition.Automations);
        }

        var statusNames = statuses.Select(x => x.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (transitions.Any(x => !statusNames.Contains(x.FromStatus.Trim()) || !statusNames.Contains(x.ToStatus.Trim())))
        {
            throw new ValidationException("Every transition status must be declared in the workflow status list.");
        }

        var todo = statuses.Where(x => NormalizeCategory(x.Category) == "Todo").Select(x => x.Name.Trim()).ToList();
        var done = statuses.Where(x => NormalizeCategory(x.Category) == "Done").Select(x => x.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (todo.Count == 0 || done.Count == 0)
        {
            throw new ValidationException("Workflow must contain at least one Todo and one Done status.");
        }

        var adjacency = transitions
            .GroupBy(x => x.FromStatus.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(edge => edge.ToStatus.Trim()).ToList(), StringComparer.OrdinalIgnoreCase);
        var reachableFromStart = Traverse(todo, adjacency);
        if (statusNames.Any(x => !reachableFromStart.Contains(x)))
        {
            throw new ConflictException("WORKFLOW_STATUS_UNREACHABLE", "Every workflow status must be reachable from a Todo status.");
        }

        foreach (var status in statusNames.Where(x => !done.Contains(x)))
        {
            if (!Traverse([status], adjacency).Overlaps(done))
            {
                throw new ConflictException("WORKFLOW_DONE_UNREACHABLE", $"Status '{status}' cannot reach a Done status.");
            }
        }
    }

    private static IReadOnlyCollection<WorkflowStatusRequest> NormalizeStatuses(
        IReadOnlyCollection<WorkflowStatusRequest>? statuses,
        IReadOnlyCollection<WorkflowTransitionRequest> transitions)
    {
        var result = statuses?.ToList() ?? transitions
            .SelectMany(x => new[] { x.FromStatus, x.ToStatus })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new WorkflowStatusRequest(name, InferCategory(name)))
            .ToList();
        if (result.Count is 0 or > 100 || result.Any(x => string.IsNullOrWhiteSpace(x.Name)))
        {
            throw new ValidationException("Workflow must contain between 1 and 100 named statuses.");
        }

        if (result.GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
        {
            throw new ConflictException("WORKFLOW_STATUS_DUPLICATE", "Workflow status names must be unique.");
        }

        foreach (var status in result)
        {
            _ = NormalizeCategory(status.Category);
        }

        return result;
    }

    private static HashSet<string> Traverse(
        IEnumerable<string> starts,
        IReadOnlyDictionary<string, List<string>> adjacency)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(starts);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current)) continue;
            if (adjacency.TryGetValue(current, out var targets))
            {
                foreach (var target in targets) pending.Push(target);
            }
        }

        return visited;
    }

    private static string InferCategory(string name) =>
        name.Trim().Equals("Done", StringComparison.OrdinalIgnoreCase)
            || name.Trim().Equals("Closed", StringComparison.OrdinalIgnoreCase)
                ? "Done"
                : name.Trim().Equals("To Do", StringComparison.OrdinalIgnoreCase)
                    || name.Trim().Equals("Backlog", StringComparison.OrdinalIgnoreCase)
                    || name.Trim().Equals("Open", StringComparison.OrdinalIgnoreCase)
                        ? "Todo"
                        : "InProgress";

    private static string NormalizeCategory(string category) => category.Trim().ToLowerInvariant() switch
    {
        "todo" => "Todo",
        "inprogress" or "in-progress" => "InProgress",
        "done" => "Done",
        _ => throw new ValidationException("Workflow status category must be Todo, InProgress or Done.")
    };

    private static IReadOnlyCollection<WorkflowAutomationRequest> NormalizeAutomations(
        IReadOnlyCollection<WorkflowAutomationRequest>? automations)
    {
        if (automations is null)
        {
            return [];
        }

        if (automations.Count > 10)
        {
            throw new ValidationException("A transition cannot contain more than 10 automations.");
        }

        return automations.Select(automation =>
        {
            var action = automation.Action?.Trim().ToLowerInvariant() switch
            {
                "assigntoactor" or "assign-to-actor" => "AssignToActor",
                "clearassignee" or "clear-assignee" => "ClearAssignee",
                "addlabel" or "add-label" => "AddLabel",
                "removelabel" or "remove-label" => "RemoveLabel",
                _ => throw new ValidationException("Workflow automation action is not supported.")
            };
            var value = string.IsNullOrWhiteSpace(automation.Value) ? null : automation.Value.Trim();
            if (action is "AddLabel" or "RemoveLabel" && (value is null || value.Length > 50))
            {
                throw new ValidationException("Label automations require a value of at most 50 characters.");
            }

            if (action is "AssignToActor" or "ClearAssignee" && value is not null)
            {
                throw new ValidationException("Assignee automations do not accept a value.");
            }

            return new WorkflowAutomationRequest(action, value);
        }).ToList();
    }
}
