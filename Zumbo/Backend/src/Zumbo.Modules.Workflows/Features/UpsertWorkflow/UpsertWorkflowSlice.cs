using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

internal sealed class UpsertWorkflowSlice(
    IDocumentRepository<WorkflowDefinitionDocument> workflows,
    IWorkflowProjectAccessChecker accessChecker,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    IWorkflowAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions,
    IWorkflowPublicationGuard? publicationGuard)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal async Task<WorkflowResponse> HandleAsync(
        CreateWorkflowRequest request,
        string correlationId,
        CancellationToken ct)
    {
        UpsertWorkflowValidator.Validate(request);
        await accessChecker.EnsureCanManageAsync(request.ProjectId, ct);
        await using var workflowLock = await AcquireLockAsync(request.ProjectId, ct);
        var previous = await workflows.SelectAsync(x => x.ProjectId == request.ProjectId, ct);
        var workflow = await SaveDraftCoreAsync(request, ct);
        var result = await PublishCoreAsync(workflow, ct);
        await audit.WriteAsync(
            request.ProjectId,
            previous is null ? null : $"v{Math.Max(previous.PublishedVersion, 1)}",
            DescribePublishedRetention(result),
            correlationId,
            ct);
        return result;
    }

    private async Task<WorkflowDefinitionDocument> SaveDraftCoreAsync(
        CreateWorkflowRequest request,
        CancellationToken ct)
    {
        var definition = WorkflowDefinitionAggregate.Define(
            request.ProjectId,
            request.Statuses,
            request.Transitions,
            clock.UtcNow);
        var schemes = WorkflowIssueTypeSchemes.Normalize(
            request.IssueTypeSchemes,
            definition.Statuses,
            definition.Transitions);
        var workflow = await workflows.SelectAsync(x => x.ProjectId == request.ProjectId, ct)
            ?? BuildDefaultDocument(request.ProjectId, definition.DefinedAt);
        workflow.Draft = WorkflowDocumentMapper.ToVersion(
            definition,
            schemes,
            Math.Max(workflow.PublishedVersion, 0) + 1);
        workflow.UpdatedAt = definition.DefinedAt;
        await PersistAsync(workflow, ct);
        return workflow;
    }

    private async Task<WorkflowResponse> PublishCoreAsync(
        WorkflowDefinitionDocument workflow,
        CancellationToken ct)
    {
        var draft = workflow.Draft
            ?? throw new ConflictException("WORKFLOW_DRAFT_REQUIRED", "Create a workflow draft before publishing.");
        if (publicationGuard is not null)
        {
            await publicationGuard.ValidateAsync(WorkflowDocumentMapper.ToCandidate(workflow.ProjectId, draft), ct);
        }

        var publishedAt = clock.UtcNow;
        var published = WorkflowDocumentMapper.CopyPublished(draft, publishedAt);
        workflow.Statuses = published.Statuses;
        workflow.Transitions = published.Transitions;
        workflow.IssueTypeSchemes = published.IssueTypeSchemes;
        workflow.PublishedVersion = published.Number;
        workflow.PublishedVersions.Add(published);
        WorkflowRetentionPolicy.RetainPublishedVersions(workflow.PublishedVersions);
        workflow.Draft = null;
        workflow.UpdatedAt = publishedAt;
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

    private static string DescribePublishedRetention(WorkflowResponse workflow)
    {
        var oldest = workflow.OldestRetainedPublishedVersion ?? workflow.PublishedVersion;
        return $"published-v{workflow.PublishedVersion};retained=v{oldest}-v{workflow.PublishedVersion};limit={workflow.PublishedVersionRetentionLimit}";
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
