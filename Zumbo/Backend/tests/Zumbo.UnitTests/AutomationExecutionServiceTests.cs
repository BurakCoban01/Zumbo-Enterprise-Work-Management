using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.Workflows.Application.Features.RunQueries;
using Zumbo.Modules.Workflows.Application.Features.RunReplay;
using Zumbo.Modules.Workflows.Application.Features.RunRetry;
using Zumbo.Modules.Workflows.Application.Features.ActionExecution;
using Zumbo.Modules.Workflows.Application.Features.RunResume;
using Zumbo.Modules.Workflows.Application.Features.ScheduleClaims;
using Zumbo.Modules.Workflows.Application.Features.RunExecution;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class AutomationExecutionServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_DeduplicatesTriggerAndPersistsStepHistory()
    {
        var fixture = await Fixture.CreateAsync();
        var context = fixture.Context("trigger-1");

        var first = Assert.Single(await fixture.ExecuteHandler.HandleAsync(
            new ExecuteAutomationCommand(context),
            default));
        var duplicate = Assert.Single(await fixture.ExecuteHandler.HandleAsync(
            new ExecuteAutomationCommand(context),
            default));

        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal(AutomationRunStates.Succeeded, first.Status);
        Assert.Equal(1, first.Attempt);
        Assert.Equal(AutomationStepStates.Succeeded, Assert.Single(first.Steps).Status);
        Assert.Single(fixture.Executor.Executions);
    }

    [Fact]
    public async Task Execute_RecordsConditionAndChainSkipsWithoutCallingActions()
    {
        var fixture = await Fixture.CreateAsync(conditionValue: "Critical");

        var conditionMismatch = Assert.Single(await fixture.ExecuteHandler.HandleAsync(
            new ExecuteAutomationCommand(fixture.Context("trigger-condition", priority: "High")),
            default));
        var loopPrevented = Assert.Single(await fixture.ExecuteHandler.HandleAsync(
            new ExecuteAutomationCommand(fixture.Context(
                "trigger-loop",
                priority: "Critical",
                visitedRuleIds: [fixture.Rule.Id])),
            default));
        var depthExceeded = Assert.Single(await fixture.ExecuteHandler.HandleAsync(
            new ExecuteAutomationCommand(fixture.Context(
                "trigger-depth",
                priority: "Critical",
                chainDepth: 3)),
            default));
        var actorUnavailable = Assert.Single(await fixture.ExecuteHandler.HandleAsync(
            new ExecuteAutomationCommand(fixture.Context(
                "trigger-actor",
                priority: "Critical",
                actorAvailable: false)),
            default));

        Assert.Equal(AutomationRunStates.Skipped, conditionMismatch.Status);
        Assert.Equal("ConditionMismatch", conditionMismatch.Outcome);
        Assert.Equal("LoopPrevented", loopPrevented.Outcome);
        Assert.Equal("ChainDepthExceeded", depthExceeded.Outcome);
        Assert.Equal("ActorUnavailable", actorUnavailable.Outcome);
        Assert.Empty(fixture.Executor.Executions);
    }

    [Fact]
    public async Task Retry_ResumesFailedStepThenDeadLettersAndAllowsManagedReplay()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Executor.FailuresRemaining = 3;

        var first = Assert.Single(await fixture.ExecuteHandler.HandleAsync(
            new ExecuteAutomationCommand(fixture.Context("trigger-retry")),
            default));
        Assert.Equal(AutomationRunStates.RetryScheduled, first.Status);
        Assert.Equal("Unexpected", first.FailureCategory);
        Assert.Empty(await fixture.DueRetriesHandler.HandleAsync(
            new ListDueAutomationRetriesQuery(500),
            default));

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var dueRetry = Assert.Single(await fixture.DueRetriesHandler.HandleAsync(
            new ListDueAutomationRetriesQuery(500),
            default));
        Assert.Equal(first.Id, dueRetry.RunId);
        Assert.Equal("organization-1", dueRetry.OrganizationId);
        var second = await fixture.ResumeHandler.HandleAsync(
            new ResumeAutomationRunCommand(first.Id, ActorAvailable: true),
            default);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        var third = await fixture.ResumeHandler.HandleAsync(
            new ResumeAutomationRunCommand(first.Id, ActorAvailable: true),
            default);
        Assert.Equal(AutomationRunStates.DeadLetter, third.Status);
        Assert.Equal(3, third.Attempt);

        var replay = await fixture.ReplayHandler.HandleAsync(
            new ReplayAutomationRunCommand(first.Id, "manual-replay"),
            default);
        Assert.Equal(AutomationRunStates.RetryScheduled, replay.Status);
        Assert.Equal(0, replay.Attempt);
        Assert.Contains("AutomationRunReplayed", fixture.Audit.Actions);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var succeeded = await fixture.ResumeHandler.HandleAsync(
            new ResumeAutomationRunCommand(first.Id, ActorAvailable: true),
            default);
        Assert.Equal(AutomationRunStates.Succeeded, succeeded.Status);
        Assert.Equal(1, succeeded.Attempt);
        Assert.Equal(4, fixture.Executor.Executions.Count);
    }

    [Fact]
    public async Task Execute_EnforcesHourlyLimitAndTenantScopedRunListing()
    {
        var fixture = await Fixture.CreateAsync(maximumExecutionsPerHour: 1);
        var first = Assert.Single(await fixture.ExecuteHandler.HandleAsync(
            new ExecuteAutomationCommand(fixture.Context("trigger-limit-1")),
            default));
        var limited = Assert.Single(await fixture.ExecuteHandler.HandleAsync(
            new ExecuteAutomationCommand(fixture.Context("trigger-limit-2")),
            default));
        var page = await fixture.Service.ListAsync(
            "project-1",
            fixture.Rule.Id,
            null,
            1,
            20,
            default);

        Assert.Equal(AutomationRunStates.Succeeded, first.Status);
        Assert.Equal("HourlyLimitExceeded", limited.Outcome);
        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Single(fixture.Executor.Executions);
    }

    [Fact]
    public async Task ClaimDueSchedules_AdvancesFromScheduledTimeWithoutDriftOrDuplicateClaim()
    {
        var scheduledFor = Start.AddMinutes(-65);
        var fixture = await Fixture.CreateAsync(
            scheduleIntervalMinutes: 30,
            nextRunAtUtc: scheduledFor);

        var firstClaim = Assert.Single(await fixture.ClaimSchedulesHandler.HandleAsync(
            new ClaimDueSchedulesQuery(10),
            default));
        var secondClaim = await fixture.ClaimSchedulesHandler.HandleAsync(
            new ClaimDueSchedulesQuery(10),
            default);
        fixture.Clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
        var recoveredClaim = Assert.Single(
            await fixture.ClaimSchedulesHandler.HandleAsync(
                new ClaimDueSchedulesQuery(10),
                default));
        var staleCompletion = await fixture.CompleteScheduleHandler.HandleAsync(
            new CompleteScheduleClaimCommand(
                firstClaim.RuleId,
                firstClaim.ScheduledForUtc,
                firstClaim.ClaimToken),
            default);
        var completed = await fixture.CompleteScheduleHandler.HandleAsync(
            new CompleteScheduleClaimCommand(
                recoveredClaim.RuleId,
                recoveredClaim.ScheduledForUtc,
                recoveredClaim.ClaimToken),
            default);
        var updatedRule = await fixture.GetRuleAsync();

        Assert.Equal(fixture.Rule.Id, firstClaim.RuleId);
        Assert.Equal(scheduledFor, firstClaim.ScheduledForUtc);
        Assert.Equal(scheduledFor, recoveredClaim.ScheduledForUtc);
        Assert.NotEqual(firstClaim.ClaimToken, recoveredClaim.ClaimToken);
        Assert.False(staleCompletion);
        Assert.True(completed);
        Assert.Equal(Start.AddMinutes(25), updatedRule.NextRunAtUtc);
        Assert.Empty(secondClaim);
    }

    [Fact]
    public async Task GetAutomationRunHandler_ReturnsTenantScopedRun()
    {
        var runs = new InMemoryDocumentRepository<AutomationRunDocument>();
        await runs.CreateAsync(new AutomationRunDocument
        {
            Id = "run-query-1",
            OrganizationId = "organization-1",
            ProjectId = "project-1",
            RuleId = "rule-query-1",
            RuleVersion = 2,
            RuleName = "Query rule",
            TriggerType = "Event",
            TriggerId = "trigger-query-1",
            SourceId = "work-query-1",
            ActorUserId = "user-1",
            RootRunId = "run-query-1",
            Status = AutomationRunStates.Succeeded,
            Outcome = AutomationRunStates.Succeeded,
            CreatedAtUtc = Start
        });
        var handler = new GetAutomationRunHandler(runs, new FixedAccess());

        var response = await handler.HandleAsync(
            new GetAutomationRunQuery("run-query-1"),
            default);

        Assert.Equal("run-query-1", response.Id);
        Assert.Equal("project-1", response.ProjectId);
        Assert.Equal(2, response.RuleVersion);
        Assert.Equal(AutomationRunStates.Succeeded, response.Status);
    }

    [Fact]
    public async Task ListAutomationRunsHandler_AppliesRuleStatusAndTenantFilters()
    {
        var runs = new InMemoryDocumentRepository<AutomationRunDocument>();
        foreach (var run in new[]
        {
            new AutomationRunDocument
            {
                Id = "run-list-1", OrganizationId = "organization-1", ProjectId = "project-1",
                RuleId = "rule-list", RuleName = "List rule", TriggerType = "Event",
                TriggerId = "trigger-list-1", SourceId = "work-list-1", ActorUserId = "user-1",
                RootRunId = "run-list-1", Status = AutomationRunStates.Succeeded,
                Outcome = AutomationRunStates.Succeeded, CreatedAtUtc = Start
            },
            new AutomationRunDocument
            {
                Id = "run-list-2", OrganizationId = "organization-1", ProjectId = "project-1",
                RuleId = "other-rule", RuleName = "Other rule", TriggerType = "Event",
                TriggerId = "trigger-list-2", SourceId = "work-list-2", ActorUserId = "user-1",
                RootRunId = "run-list-2", Status = AutomationRunStates.DeadLetter,
                Outcome = AutomationRunStates.DeadLetter, CreatedAtUtc = Start.AddMinutes(1)
            }
        })
        {
            await runs.CreateAsync(run);
        }
        var handler = new ListAutomationRunsHandler(runs, new FixedAccess());

        var response = await handler.HandleAsync(
            new ListAutomationRunsQuery(
                "project-1",
                " rule-list ",
                " Succeeded ",
                0,
                500),
            default);

        var item = Assert.Single(response.Items);
        Assert.Equal("run-list-1", item.Id);
        Assert.Equal(1, response.Page);
        Assert.Equal(100, response.PageSize);
        Assert.Equal(1, response.Total);
    }

    private sealed class Fixture
    {
        private readonly InMemoryDocumentRepository<AutomationRuleDocument> rules = new();
        private readonly InMemoryDocumentRepository<AutomationRunDocument> runs = new();

        public MutableClock Clock { get; } = new();
        public CapturingActionExecutor Executor { get; } = new();
        public CapturingAudit Audit { get; } = new();
        public AutomationRuleDocument Rule { get; private set; } = null!;
        public AutomationExecutionService Service { get; private set; } = null!;
        public ReplayAutomationRunHandler ReplayHandler { get; private set; } = null!;
        public ListDueAutomationRetriesHandler DueRetriesHandler { get; private set; } = null!;
        public ResumeAutomationRunHandler ResumeHandler { get; private set; } = null!;
        public ClaimDueSchedulesHandler ClaimSchedulesHandler { get; private set; } = null!;
        public CompleteScheduleClaimHandler CompleteScheduleHandler { get; private set; } = null!;
        public ExecuteAutomationHandler ExecuteHandler { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(
            string? conditionValue = null,
            int maximumExecutionsPerHour = 100,
            int? scheduleIntervalMinutes = null,
            DateTimeOffset? nextRunAtUtc = null)
        {
            var fixture = new Fixture();
            var condition = conditionValue is null
                ? null
                : new AutomationConditionDocument
                {
                    Kind = "Field",
                    Field = "Priority",
                    Operator = "Equals",
                    Value = conditionValue
                };
            fixture.Rule = await fixture.rules.CreateAsync(new AutomationRuleDocument
            {
                Id = "rule-1",
                OrganizationId = "organization-1",
                ProjectId = "project-1",
                Active = true,
                NextRunAtUtc = nextRunAtUtc,
                PublishedVersion = 1,
                PublishedVersions =
                [
                    new AutomationRuleVersionDocument
                    {
                        Number = 1,
                        State = "Published",
                        Name = "Escalate",
                        Trigger = new AutomationTriggerDocument
                        {
                            Type = scheduleIntervalMinutes.HasValue ? "Schedule" : "Event",
                            EventType = scheduleIntervalMinutes.HasValue ? null : "WorkItemTransitioned",
                            IntervalMinutes = scheduleIntervalMinutes,
                            StartAtUtc = scheduleIntervalMinutes.HasValue ? Start : null
                        },
                        Condition = condition,
                        Actions =
                        [
                            new AutomationActionDocument
                            {
                                Type = "AddLabel",
                                Value = "automated"
                            }
                        ],
                        MaximumExecutionsPerHour = maximumExecutionsPerHour,
                        MaximumChainDepth = 3,
                        CreatedAt = Start,
                        PublishedAt = Start
                    }
                ],
                CreatedByUserId = "user-1",
                CreatedAt = Start,
                UpdatedAt = Start
            });
            var access = new FixedAccess();
            var locks = new InMemoryDistributedLockProvider();
            var lockOptions = Options.Create(new DistributedLockOptions());
            fixture.Service = new AutomationExecutionService(
                fixture.rules,
                fixture.runs,
                access,
                fixture.Executor,
                locks,
                lockOptions,
                fixture.Clock,
                fixture.Audit);
            fixture.ReplayHandler = new ReplayAutomationRunHandler(
                fixture.runs,
                access,
                locks,
                lockOptions,
                fixture.Clock,
                fixture.Audit);
            fixture.DueRetriesHandler = new ListDueAutomationRetriesHandler(
                fixture.runs,
                fixture.Clock);
            fixture.ResumeHandler = new ResumeAutomationRunHandler(
                fixture.rules,
                fixture.runs,
                locks,
                lockOptions,
                fixture.Clock,
                new AutomationRunActionExecutor(fixture.runs, fixture.Executor, fixture.Clock));
            fixture.ClaimSchedulesHandler = new ClaimDueSchedulesHandler(
                fixture.rules,
                locks,
                lockOptions,
                fixture.Clock);
            fixture.CompleteScheduleHandler = new CompleteScheduleClaimHandler(
                fixture.rules,
                locks,
                lockOptions,
                fixture.Clock);
            fixture.ExecuteHandler = new ExecuteAutomationHandler(
                fixture.rules,
                fixture.runs,
                locks,
                lockOptions,
                fixture.Clock,
                new AutomationRunActionExecutor(fixture.runs, fixture.Executor, fixture.Clock));
            return fixture;
        }

        public async Task<AutomationRuleDocument> GetRuleAsync() =>
            (await rules.SelectAsync(rule => rule.Id == Rule.Id, default))!;

        public AutomationExecutionContext Context(
            string triggerId,
            string priority = "High",
            IReadOnlyCollection<string>? visitedRuleIds = null,
            int chainDepth = 0,
            bool actorAvailable = true) =>
            new(
                "organization-1",
                "project-1",
                "Event",
                "WorkItemTransitioned",
                triggerId,
                "work-1",
                "user-1",
                "correlation-1",
                Clock.UtcNow,
                new Dictionary<string, string?>
                {
                    ["Status"] = "In Progress",
                    ["PreviousStatus"] = "To Do",
                    ["Priority"] = priority,
                    ["Type"] = "Task",
                    ["AssigneeUserId"] = null,
                    ["Labels"] = "triage"
                },
                ActorAvailable: actorAvailable,
                ChainDepth: chainDepth,
                VisitedRuleIds: visitedRuleIds);
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = Start;
        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }

    private sealed class FixedAccess : IAutomationProjectAccessChecker
    {
        public Task<AutomationProjectScope> EnsureCanViewAsync(string projectId, CancellationToken ct) =>
            Task.FromResult(new AutomationProjectScope("organization-1", "user-1"));

        public Task<AutomationProjectScope> EnsureCanManageAsync(string projectId, CancellationToken ct) =>
            Task.FromResult(new AutomationProjectScope("organization-1", "user-1"));
    }

    private sealed class CapturingActionExecutor : IAutomationActionExecutor
    {
        public int FailuresRemaining { get; set; }
        public List<AutomationActionExecution> Executions { get; } = [];

        public Task ExecuteAsync(AutomationActionExecution execution, CancellationToken ct)
        {
            Executions.Add(execution);
            if (FailuresRemaining-- > 0)
                throw new InvalidOperationException("Synthetic action failure.");
            return Task.CompletedTask;
        }
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
}
