using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class AutomationRuleServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DraftPublishAndEdit_PreserveImmutablePublishedVersion()
    {
        var fixture = new Fixture();
        var draft = await fixture.Service.SaveDraftAsync(null, EventRule("Initial rule"), "draft-1", default);
        var published = await fixture.Service.PublishAsync(draft.Id, "publish-1", default);
        var editedDraft = await fixture.Service.SaveDraftAsync(
            draft.Id,
            EventRule("Edited rule"),
            "draft-2",
            default);
        var stillPublished = await fixture.Service.GetAsync(draft.Id, draft: false, default);

        Assert.Equal(1, published.PublishedVersion);
        Assert.True(published.Active);
        Assert.Equal("Initial rule", published.Definition!.Name);
        Assert.Equal(2, editedDraft.Definition!.Number);
        Assert.Equal("Edited rule", editedDraft.Definition.Name);
        Assert.Equal("Initial rule", stillPublished.Definition!.Name);
        Assert.Equal(["AutomationDraftSaved", "AutomationPublished", "AutomationDraftSaved"], fixture.Audit.Actions);
    }

    [Fact]
    public async Task DryRun_EvaluatesBoundedTreeWithoutPersistingExecution()
    {
        var fixture = new Fixture();
        var draft = await fixture.Service.SaveDraftAsync(
            null,
            EventRule(
                "Urgent triage",
                new AutomationConditionRequest("All", Children:
                [
                    new("Field", "Priority", "Equals", "High"),
                    new("Field", "Labels", "Contains", "triage")
                ])),
            "draft",
            default);
        var before = await fixture.Service.GetAsync(draft.Id, draft: true, default);

        var matching = await fixture.Service.DryRunAsync(
            draft.Id,
            new AutomationDryRunContext(
                "Event",
                "WorkItemTransitioned",
                "work-1",
                new Dictionary<string, string?>
                {
                    ["Priority"] = "High",
                    ["Labels"] = "customer,triage"
                }),
            default);
        var mismatch = await fixture.Service.DryRunAsync(
            draft.Id,
            new AutomationDryRunContext(
                "Event",
                "WorkItemTransitioned",
                "work-1",
                new Dictionary<string, string?>
                {
                    ["Priority"] = "Low",
                    ["Labels"] = "triage"
                }),
            default);
        var after = await fixture.Service.GetAsync(draft.Id, draft: true, default);

        Assert.Equal("WouldExecute", matching.Outcome);
        Assert.True(matching.ConditionMatched);
        Assert.Equal(["AddLabel"], matching.PlannedActions.Select(action => action.Type));
        Assert.Equal("ConditionMismatch", mismatch.Outcome);
        Assert.Empty(mismatch.PlannedActions);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.Definition!.Number, after.Definition!.Number);
        Assert.Equal(before.Definition.Name, after.Definition.Name);
        Assert.Equal(
            before.Definition.Actions.Select(action => (action.Type, action.Value)),
            after.Definition.Actions.Select(action => (action.Type, action.Value)));
    }

    [Fact]
    public async Task SchedulePublishPauseResumeAndArchive_KeepExplicitState()
    {
        var fixture = new Fixture();
        var draft = await fixture.Service.SaveDraftAsync(
            null,
            new DefineAutomationRuleRequest(
                "project-1",
                "Scheduled review",
                null,
                new AutomationTriggerRequest("Schedule", IntervalMinutes: 30, StartAtUtc: Now.AddHours(1)),
                null,
                [new AutomationActionRequest("AddComment", "Review this work item.")]),
            "draft",
            default);
        var published = await fixture.Service.PublishAsync(draft.Id, "publish", default);
        var paused = await fixture.Service.SetActiveAsync(draft.Id, false, "pause", default);
        var resumed = await fixture.Service.SetActiveAsync(draft.Id, true, "resume", default);
        await fixture.Service.ArchiveAsync(draft.Id, "archive", default);
        var archived = await fixture.Service.GetAsync(draft.Id, draft: false, default);

        Assert.Equal(Now.AddHours(1), published.NextRunAtUtc);
        Assert.False(paused.Active);
        Assert.Null(paused.NextRunAtUtc);
        Assert.True(resumed.Active);
        Assert.Equal(Now.AddHours(1), resumed.NextRunAtUtc);
        Assert.True(archived.Archived);
        Assert.False(archived.Active);
        Assert.Null(archived.NextRunAtUtc);
    }

    [Fact]
    public async Task ExistingRule_FromAnotherTenant_IsRejectedAfterProjectAuthorization()
    {
        var fixture = new Fixture();
        var draft = await fixture.Service.SaveDraftAsync(null, EventRule("Tenant rule"), "draft", default);
        fixture.Access.OrganizationId = "organization-2";

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.GetAsync(draft.Id, draft: true, default));
    }

    private static DefineAutomationRuleRequest EventRule(
        string name,
        AutomationConditionRequest? condition = null) =>
        new(
            "project-1",
            name,
            null,
            new AutomationTriggerRequest("Event", "WorkItemTransitioned"),
            condition,
            [new AutomationActionRequest("AddLabel", "automated")]);

    private sealed class Fixture
    {
        public MutableAccess Access { get; } = new();
        public CapturingAudit Audit { get; } = new();
        public AutomationRuleService Service { get; }

        public Fixture()
        {
            Service = new AutomationRuleService(
                new InMemoryDocumentRepository<AutomationRuleDocument>(),
                Access,
                new InMemoryDistributedLockProvider(),
                Options.Create(new DistributedLockOptions()),
                new FixedClock(),
                Audit);
        }
    }

    private sealed class MutableAccess : IAutomationProjectAccessChecker
    {
        public string OrganizationId { get; set; } = "organization-1";

        public Task<AutomationProjectScope> EnsureCanViewAsync(string projectId, CancellationToken ct) =>
            Task.FromResult(new AutomationProjectScope(OrganizationId, "user-1"));

        public Task<AutomationProjectScope> EnsureCanManageAsync(string projectId, CancellationToken ct) =>
            Task.FromResult(new AutomationProjectScope(OrganizationId, "user-1"));
    }

    private sealed class CapturingAudit : IAutomationAuditWriter
    {
        public List<string> Actions { get; } = [];

        public Task WriteAsync(
            string action,
            string ruleId,
            string projectId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
