using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class WorkflowAggregateTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 7, 19, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Define_ValidGraph_NormalizesDefinition()
    {
        var aggregate = WorkflowDefinitionAggregate.Define(
            "project-1",
            [
                new WorkflowStatusRequest(" Open ", " todo "),
                new WorkflowStatusRequest(" Building ", "in-progress"),
                new WorkflowStatusRequest(" Released ", " done ")
            ],
            [
                new WorkflowTransitionRequest(" Open ", " Building ", true, false),
                new WorkflowTransitionRequest(
                    " Building ",
                    " Released ",
                    false,
                    true,
                    true,
                    [new WorkflowAutomationRequest(" add-label ", " released ")])
            ],
            OccurredAt);

        Assert.Equal("project-1", aggregate.Id);
        Assert.Equal("project-1", aggregate.ProjectId);
        Assert.Equal(OccurredAt, aggregate.DefinedAt);
        Assert.Equal(["Open", "Building", "Released"], aggregate.Statuses.Select(status => status.Name));
        Assert.Equal(["Todo", "InProgress", "Done"], aggregate.Statuses.Select(status => status.Category));

        var transition = Assert.Single(aggregate.Transitions, item => item.ToStatus == "Released");
        Assert.Equal("Building", transition.FromStatus);
        Assert.True(transition.RequiresCompletedChecklist);
        Assert.True(transition.RequiresApproval);
        var automation = Assert.Single(transition.Automations!);
        Assert.Equal("AddLabel", automation.Action);
        Assert.Equal("released", automation.Value);
    }

    [Fact]
    public void Define_WithoutStatuses_InfersCategories()
    {
        var aggregate = WorkflowDefinitionAggregate.Define(
            "project-1",
            null,
            [
                new WorkflowTransitionRequest("Open", "Building", false, false),
                new WorkflowTransitionRequest("Building", "Closed", false, false)
            ],
            OccurredAt);

        Assert.Collection(
            aggregate.Statuses,
            status => Assert.Equal(new WorkflowStatusRequest("Open", "Todo"), status),
            status => Assert.Equal(new WorkflowStatusRequest("Building", "InProgress"), status),
            status => Assert.Equal(new WorkflowStatusRequest("Closed", "Done"), status));
    }

    [Fact]
    public void Define_DuplicateStatus_ThrowsExistingConflict()
    {
        var error = Assert.Throws<ConflictException>(() => WorkflowDefinitionAggregate.Define(
            "project-1",
            BasicStatuses.Append(new WorkflowStatusRequest(" open ", "Todo")).ToArray(),
            [new WorkflowTransitionRequest("Open", "Done", false, false)],
            OccurredAt));

        Assert.Equal("WORKFLOW_STATUS_DUPLICATE", error.Code);
        Assert.Equal("Workflow status names must be unique.", error.Message);
    }

    [Fact]
    public void Define_DuplicateTransition_ThrowsExistingConflict()
    {
        var error = Assert.Throws<ConflictException>(() => WorkflowDefinitionAggregate.Define(
            "project-1",
            BasicStatuses,
            [
                new WorkflowTransitionRequest("Open", "Done", false, false),
                new WorkflowTransitionRequest(" open ", " done ", false, false)
            ],
            OccurredAt));

        Assert.Equal("WORKFLOW_TRANSITION_DUPLICATE", error.Code);
        Assert.Equal("Workflow transitions must be unique.", error.Message);
    }

    [Fact]
    public void Define_UnreachableStatus_ThrowsExistingConflict()
    {
        var error = Assert.Throws<ConflictException>(() => WorkflowDefinitionAggregate.Define(
            "project-1",
            BasicStatuses.Append(new WorkflowStatusRequest("Orphan", "InProgress")).ToArray(),
            [new WorkflowTransitionRequest("Open", "Done", false, false)],
            OccurredAt));

        Assert.Equal("WORKFLOW_STATUS_UNREACHABLE", error.Code);
        Assert.Equal("Every workflow status must be reachable from a Todo status.", error.Message);
    }

    [Fact]
    public void Define_StatusThatCannotReachDone_ThrowsExistingConflict()
    {
        var error = Assert.Throws<ConflictException>(() => WorkflowDefinitionAggregate.Define(
            "project-1",
            [
                new WorkflowStatusRequest("Open", "Todo"),
                new WorkflowStatusRequest("Building", "InProgress"),
                new WorkflowStatusRequest("Done", "Done")
            ],
            [
                new WorkflowTransitionRequest("Open", "Building", false, false),
                new WorkflowTransitionRequest("Open", "Done", false, false)
            ],
            OccurredAt));

        Assert.Equal("WORKFLOW_DONE_UNREACHABLE", error.Code);
        Assert.Equal("Status 'Building' cannot reach a Done status.", error.Message);
    }

    [Fact]
    public void Define_RaisesClearableEvent_AndMapperCopiesFields()
    {
        var aggregate = WorkflowDefinitionAggregate.Define(
            "project-1",
            BasicStatuses,
            [new WorkflowTransitionRequest("Open", "Done", false, false)],
            OccurredAt);

        var domainEvent = Assert.IsType<WorkflowDefinedDomainEvent>(Assert.Single(aggregate.DomainEvents));
        Assert.Equal(aggregate.Id, domainEvent.WorkflowId);
        Assert.Equal("project-1", domainEvent.ProjectId);
        Assert.Equal(2, domainEvent.StatusCount);
        Assert.Equal(1, domainEvent.TransitionCount);
        Assert.Equal(OccurredAt, domainEvent.OccurredAt);

        var integrationEvent = new WorkflowDomainEventMapper().Map(domainEvent);
        Assert.Equal(32, integrationEvent.EventId.Length);
        Assert.Equal("workflow.defined.v1", integrationEvent.EventName);
        Assert.Equal(domainEvent.WorkflowId, integrationEvent.AggregateId);
        Assert.Equal(domainEvent.ProjectId, integrationEvent.ProjectId);
        Assert.Equal(domainEvent.StatusCount, integrationEvent.StatusCount);
        Assert.Equal(domainEvent.TransitionCount, integrationEvent.TransitionCount);
        Assert.Equal(domainEvent.OccurredAt, integrationEvent.OccurredAt);

        aggregate.ClearDomainEvents();
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public async Task Service_DraftPublishAndHistory_PreservePublishedVersion()
    {
        var service = new WorkflowService(
            new InMemoryDocumentRepository<WorkflowDefinitionDocument>(),
            new AllowAccess(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            new FixedClock(OccurredAt),
            new NoopAudit());
        var initial = await service.GetOrCreateDefaultAsync("project-versioned", CancellationToken.None);
        var draft = await service.SaveDraftAsync(
            new CreateWorkflowRequest(
                "project-versioned",
                [new WorkflowTransitionRequest("Open", "Done", false, false)],
                [new WorkflowStatusRequest("Open", "Todo"), new WorkflowStatusRequest("Done", "Done")]),
            "test",
            CancellationToken.None);

        Assert.Equal(1, initial.PublishedVersion);
        Assert.Equal(2, draft.PublishedVersion);
        Assert.Equal(1, (await service.GetOrCreateDefaultAsync("project-versioned", CancellationToken.None)).PublishedVersion);

        var published = await service.PublishAsync("project-versioned", "test", CancellationToken.None);
        var versions = await service.ListVersionsAsync("project-versioned", CancellationToken.None);
        Assert.Equal(2, published.PublishedVersion);
        Assert.False(published.HasDraft);
        Assert.Equal([2, 1], versions.Select(x => x.Number));
    }

    [Fact]
    public async Task Service_IssueSchemeWithoutTodoDefault_IsRejected()
    {
        var service = new WorkflowService(
            new InMemoryDocumentRepository<WorkflowDefinitionDocument>(),
            new AllowAccess(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            new FixedClock(OccurredAt),
            new NoopAudit());
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.SaveDraftAsync(
            new CreateWorkflowRequest(
                "project-scheme",
                [new WorkflowTransitionRequest("Open", "Done", false, false)],
                [new WorkflowStatusRequest("Open", "Todo"), new WorkflowStatusRequest("Done", "Done")],
                [new WorkflowIssueTypeSchemeRequest("Task", "Done", ["Open", "Done"], ["Done"])]),
            "test",
            CancellationToken.None));

        Assert.Equal("WORKFLOW_ISSUE_SCHEME_DEFAULT_INVALID", error.Code);
    }

    private static WorkflowStatusRequest[] BasicStatuses =>
    [
        new("Open", "Todo"),
        new("Done", "Done")
    ];

    private sealed class AllowAccess : IWorkflowProjectAccessChecker
    {
        public Task EnsureCanViewAsync(string projectId, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureCanManageAsync(string projectId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoopAudit : IWorkflowAuditWriter
    {
        public Task WriteAsync(string projectId, string? oldValue, string? newValue, string correlationId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
