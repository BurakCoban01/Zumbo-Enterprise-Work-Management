using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

internal sealed class GetWorkflowSlice(
    IDocumentRepository<WorkflowDefinitionDocument> workflows,
    IWorkflowProjectAccessChecker accessChecker,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal async Task<WorkflowResponse> HandleAsync(GetWorkflowQuery query, CancellationToken ct)
    {
        GetWorkflowValidator.Validate(query);
        var projectId = query.ProjectId;
        await accessChecker.EnsureCanViewAsync(projectId, ct);
        var workflow = await workflows.SelectAsync(x => x.ProjectId == projectId, ct);
        if (workflow is not null && workflow.Statuses.Count > 0)
        {
            return WorkflowDocumentMapper.ToResponse(workflow);
        }

        await using var workflowLock = await AcquireLockAsync(projectId, ct);
        workflow = await workflows.SelectAsync(x => x.ProjectId == projectId, ct);
        if (workflow is not null && workflow.Statuses.Count > 0)
        {
            return WorkflowDocumentMapper.ToResponse(workflow);
        }

        return await CreateDefaultAsync(projectId, workflow, ct);
    }

    private async Task<WorkflowResponse> CreateDefaultAsync(
        string projectId,
        WorkflowDefinitionDocument? workflow,
        CancellationToken ct)
    {
        workflow ??= BuildDefaultDocument(projectId, clock.UtcNow);
        await PersistAsync(workflow, ct);
        return WorkflowDocumentMapper.ToResponse(workflow);
    }

    private async Task PersistAsync(WorkflowDefinitionDocument workflow, CancellationToken ct)
    {
        if (await workflows.SelectAsync(x => x.Id == workflow.Id, ct) is null)
        {
            await workflows.CreateAsync(workflow, ct);
            return;
        }

        var result = await workflows.ReplaceByVersionAsync(
            x => x.Id == workflow.Id,
            workflow,
            expectedVersion.Consume(workflow.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("WORKFLOW_NOT_FOUND", "Workflow was not found.");
        }

        workflow.Version = result.Version!.Value;
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

    private static WorkflowDefinitionDocument BuildDefaultDocument(string projectId, DateTimeOffset now)
    {
        var definition = WorkflowDefinitionAggregate.Define(projectId, DefaultStatuses, DefaultTransitions, now);
        var schemes = WorkflowIssueTypeSchemes.Normalize(null, definition.Statuses, definition.Transitions);
        var draft = WorkflowDocumentMapper.ToVersion(definition, schemes, 1);
        var published = WorkflowDocumentMapper.CopyPublished(draft, now);
        return new WorkflowDefinitionDocument
        {
            ProjectId = projectId,
            Statuses = published.Statuses,
            Transitions = published.Transitions,
            IssueTypeSchemes = published.IssueTypeSchemes,
            PublishedVersion = 1,
            PublishedVersions = [published],
            CreatedAt = now,
            UpdatedAt = now
        };
    }

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
}
