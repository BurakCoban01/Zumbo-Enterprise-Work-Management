using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public interface IWorkflowProjectAccessChecker
{
    Task EnsureCanViewAsync(string projectId, CancellationToken ct);
    Task EnsureCanManageAsync(string projectId, CancellationToken ct);
}

public interface IWorkflowAuditWriter
{
    Task WriteAsync(string projectId, string? oldValue, string? newValue, string correlationId, CancellationToken ct);
}

public sealed record CreateWorkflowRequest(
    string ProjectId,
    IReadOnlyCollection<WorkflowTransitionRequest> Transitions,
    IReadOnlyCollection<WorkflowStatusRequest>? Statuses = null);
public sealed record WorkflowStatusRequest(string Name, string Category);
public sealed record WorkflowTransitionRequest(
    string FromStatus,
    string ToStatus,
    bool RequiresAssignee,
    bool RequiresCompletedChecklist,
    bool RequiresApproval = false,
    IReadOnlyCollection<WorkflowAutomationRequest>? Automations = null);
public sealed record WorkflowAutomationRequest(string Action, string? Value = null);
public sealed record WorkflowResponse(
    string Id,
    string ProjectId,
    IReadOnlyCollection<WorkflowStatusResponse> Statuses,
    IReadOnlyCollection<WorkflowTransitionResponse> Transitions);
public sealed record WorkflowStatusResponse(string Name, string Category);
public sealed record WorkflowTransitionResponse(
    string FromStatus,
    string ToStatus,
    bool RequiresAssignee,
    bool RequiresCompletedChecklist,
    bool RequiresApproval,
    IReadOnlyCollection<WorkflowAutomationResponse> Automations);
public sealed record WorkflowAutomationResponse(string Action, string? Value);

public sealed class WorkflowDefinitionDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public List<WorkflowStatusDocument> Statuses { get; set; } = [];
    public List<WorkflowTransitionDocument> Transitions { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class WorkflowStatusDocument
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public sealed class WorkflowTransitionDocument
{
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public bool RequiresAssignee { get; set; }
    public bool RequiresCompletedChecklist { get; set; }
    public bool RequiresApproval { get; set; }
    public List<WorkflowAutomationDocument> Automations { get; set; } = [];
}

public sealed class WorkflowAutomationDocument
{
    public string Action { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public sealed class WorkflowService(
    IDocumentRepository<WorkflowDefinitionDocument> workflows,
    IWorkflowProjectAccessChecker accessChecker,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    IWorkflowAuditWriter audit)
{
    public Task<WorkflowResponse> UpsertAsync(CreateWorkflowRequest request, CancellationToken ct) =>
        UpsertAsync(request, "none", ct);

    public async Task<WorkflowResponse> UpsertAsync(CreateWorkflowRequest request, string correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId) || request.Transitions.Count == 0)
        {
            throw new ValidationException("Project id and transitions are required.");
        }

        await accessChecker.EnsureCanManageAsync(request.ProjectId, ct);
        var statuses = NormalizeStatuses(request.Statuses, request.Transitions);
        ValidateWorkflow(statuses, request.Transitions);

        await using var workflowLock = await AcquireLockAsync(request.ProjectId, ct);
        var previous = await workflows.SelectAsync(x => x.ProjectId == request.ProjectId, ct);
        var oldValue = previous is null ? null : $"{previous.Statuses.Count}:{previous.Transitions.Count}";
        var result = await SaveAsync(request.ProjectId, statuses, request.Transitions, ct);
        await audit.WriteAsync(request.ProjectId, oldValue, $"{result.Statuses.Count}:{result.Transitions.Count}", correlationId, ct);
        return result;
    }

    public async Task<WorkflowResponse> GetOrCreateDefaultAsync(string projectId, CancellationToken ct)
    {
        await accessChecker.EnsureCanViewAsync(projectId, ct);
        var workflow = await workflows.SelectAsync(x => x.ProjectId == projectId, ct);
        if (workflow is not null && workflow.Statuses.Count > 0)
        {
            return ToResponse(workflow);
        }

        await using var workflowLock = await AcquireLockAsync(projectId, ct);
        workflow = await workflows.SelectAsync(x => x.ProjectId == projectId, ct);
        if (workflow is null)
        {
            return await SaveAsync(projectId, DefaultStatuses, DefaultTransitions, ct);
        }

        if (workflow.Statuses.Count == 0)
        {
            var transitions = workflow.Transitions.Select(x => new WorkflowTransitionRequest(
                x.FromStatus,
                x.ToStatus,
                x.RequiresAssignee,
                x.RequiresCompletedChecklist,
                x.RequiresApproval,
                x.Automations?.Select(automation =>
                    new WorkflowAutomationRequest(automation.Action, automation.Value)).ToList())).ToList();
            var statuses = NormalizeStatuses(null, transitions);
            ValidateWorkflow(statuses, transitions);
            return await SaveAsync(projectId, statuses, transitions, ct);
        }

        return ToResponse(workflow);
    }

    public async Task<IReadOnlyCollection<WorkflowTransitionResponse>> GetTransitionsAsync(string projectId, CancellationToken ct) =>
        (await GetOrCreateDefaultAsync(projectId, ct)).Transitions;

    private static readonly WorkflowTransitionRequest[] DefaultTransitions =
    [
        new("To Do", "In Progress", false, false),
        new("In Progress", "Code Review", true, false),
        new("In Progress", "Blocked", false, false),
        new("In Progress", "To Do", false, false),
        new("Blocked", "In Progress", false, false),
        new("Blocked", "To Do", false, false),
        new("Code Review", "Test", true, false),
        new("Code Review", "In Progress", false, false),
        new("Test", "Done", false, true),
        new("Test", "Code Review", false, false)
    ];

    private static readonly WorkflowStatusRequest[] DefaultStatuses =
    [
        new("To Do", "Todo"),
        new("In Progress", "InProgress"),
        new("Blocked", "InProgress"),
        new("Code Review", "InProgress"),
        new("Test", "InProgress"),
        new("Done", "Done")
    ];

    private async Task<WorkflowResponse> SaveAsync(
        string projectId,
        IReadOnlyCollection<WorkflowStatusRequest> statuses,
        IReadOnlyCollection<WorkflowTransitionRequest> transitions,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var workflow = await workflows.SelectAsync(x => x.ProjectId == projectId, ct)
            ?? new WorkflowDefinitionDocument { ProjectId = projectId, CreatedAt = now };
        workflow.Statuses = statuses.Select(x => new WorkflowStatusDocument
        {
            Name = x.Name.Trim(),
            Category = NormalizeCategory(x.Category)
        }).ToList();
        workflow.Transitions = transitions.Select(x => new WorkflowTransitionDocument
        {
            FromStatus = x.FromStatus.Trim(),
            ToStatus = x.ToStatus.Trim(),
            RequiresAssignee = x.RequiresAssignee,
            RequiresCompletedChecklist = x.RequiresCompletedChecklist,
            RequiresApproval = x.RequiresApproval,
            Automations = NormalizeAutomations(x.Automations)
                .Select(automation => new WorkflowAutomationDocument
                {
                    Action = automation.Action,
                    Value = automation.Value
                }).ToList()
        }).ToList();
        workflow.UpdatedAt = now;

        if (await workflows.SelectAsync(x => x.Id == workflow.Id, ct) is null)
        {
            await workflows.CreateAsync(workflow, ct);
        }
        else
        {
            await workflows.ReplaceByFilterAsync(x => x.Id == workflow.Id, workflow, ct);
        }

        return ToResponse(workflow);
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

    private async Task<IAsyncDisposable> AcquireLockAsync(string projectId, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            "workflow:" + projectId,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("WORKFLOW_RESOURCE_BUSY", "Workflow is busy; retry the operation.");
    }

    private static WorkflowResponse ToResponse(WorkflowDefinitionDocument workflow) =>
        new(
            workflow.Id,
            workflow.ProjectId,
            workflow.Statuses.Select(x => new WorkflowStatusResponse(x.Name, x.Category)).ToList(),
            workflow.Transitions.Select(x =>
                new WorkflowTransitionResponse(
                    x.FromStatus,
                    x.ToStatus,
                    x.RequiresAssignee,
                    x.RequiresCompletedChecklist,
                    x.RequiresApproval,
                    (x.Automations ?? []).Select(automation =>
                        new WorkflowAutomationResponse(automation.Action, automation.Value)).ToList())).ToList());
}
