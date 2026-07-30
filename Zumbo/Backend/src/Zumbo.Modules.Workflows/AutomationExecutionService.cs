using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationExecutionContext(
    string OrganizationId,
    string ProjectId,
    string TriggerType,
    string? EventType,
    string TriggerId,
    string SourceId,
    string ActorUserId,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, string?> Fields,
    bool ActorAvailable = true,
    string? RootRunId = null,
    int ChainDepth = 0,
    IReadOnlyCollection<string>? VisitedRuleIds = null,
    string? RuleId = null);

public sealed record AutomationActionExecution(
    string RunId,
    string RuleId,
    int RuleVersion,
    string ProjectId,
    string SourceId,
    string ActorUserId,
    string RootRunId,
    int ChainDepth,
    IReadOnlyCollection<string> VisitedRuleIds,
    int ActionIndex,
    AutomationActionDefinition Action,
    string CorrelationId);

public interface IAutomationActionExecutor
{
    Task ExecuteAsync(AutomationActionExecution execution, CancellationToken ct);
}

public sealed record AutomationScheduledSource(
    string SourceId,
    IReadOnlyDictionary<string, string?> Fields);

public interface IAutomationScheduledSourceProvider
{
    Task<IReadOnlyCollection<AutomationScheduledSource>> ListAsync(
        string projectId,
        int maximumSources,
        CancellationToken ct);
}

public sealed record AutomationScheduleDispatch(
    string RuleId,
    int RuleVersion,
    string RuleName,
    string OrganizationId,
    string ProjectId,
    string ActorUserId,
    DateTimeOffset ScheduledForUtc,
    string ClaimToken);

public sealed class AutomationRuntimeOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 15;
    public int BatchSize { get; init; } = 50;
    public int MaximumScheduledSourcesPerRule { get; init; } = 1000;
}

public sealed record AutomationRunStepResponse(
    int Index,
    string ActionType,
    string Status,
    int Attempt,
    string? FailureCategory,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record AutomationRunResponse(
    string Id,
    string ProjectId,
    string RuleId,
    int RuleVersion,
    string RuleName,
    string TriggerType,
    string? EventType,
    string SourceId,
    string ActorUserId,
    string RootRunId,
    int ChainDepth,
    string Status,
    string Outcome,
    int Attempt,
    int MaximumAttempts,
    string? FailureCategory,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    IReadOnlyCollection<AutomationRunStepResponse> Steps,
    long Version) : IVersionedResource;

public sealed record AutomationRunPageResponse(
    IReadOnlyCollection<AutomationRunResponse> Items,
    int Page,
    int PageSize,
    long Total);

public sealed class AutomationExecutionService(
    IDocumentRepository<AutomationRuleDocument> rules,
    IDocumentRepository<AutomationRunDocument> runs,
    IAutomationProjectAccessChecker access,
    IAutomationActionExecutor actions,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    IAutomationAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private const int MaximumRunAttempts = 3;
    private static readonly TimeSpan ScheduleClaimDuration = TimeSpan.FromMinutes(5);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<IReadOnlyCollection<AutomationRunResponse>> ExecuteAsync(
        AutomationExecutionContext context,
        CancellationToken ct)
    {
        ValidateContext(context);
        var matchingRules = await ListMatchingRulesAsync(context, ct);
        var responses = new List<AutomationRunResponse>(matchingRules.Count);
        foreach (var rule in matchingRules)
        {
            responses.Add(await ExecuteRuleAsync(rule, context, ct));
        }

        return responses;
    }

    public async Task<AutomationRunResponse> ResumeAsync(
        string runId,
        bool actorAvailable,
        CancellationToken ct)
    {
        await using var resourceLock = await AcquireAsync($"automation-run:{runId}", ct);
        var run = await GetRunAsync(runId, ct);
        if (run.Status != AutomationRunStates.RetryScheduled)
        {
            return ToResponse(run);
        }

        if (run.NextAttemptAtUtc is { } next && next > clock.UtcNow)
        {
            return ToResponse(run);
        }

        var rule = await rules.SelectAsync(
            candidate => candidate.Id == run.RuleId
                && candidate.OrganizationId == run.OrganizationId
                && candidate.ProjectId == run.ProjectId,
            ct);
        var definition = rule?.PublishedVersions.SingleOrDefault(
            version => version.Number == run.RuleVersion);
        if (definition is null)
        {
            return await SkipAsync(run, "RuleVersionUnavailable", ct);
        }

        if (!actorAvailable)
        {
            return await SkipAsync(run, "ActorUnavailable", ct);
        }

        return await ExecuteActionsAsync(run, definition, ct);
    }

    public async Task<AutomationRunResponse> ReplayAsync(
        string runId,
        string correlationId,
        CancellationToken ct)
    {
        await using var resourceLock = await AcquireAsync($"automation-run:{runId}", ct);
        var run = await GetRunAsync(runId, ct);
        var scope = await access.EnsureCanManageAsync(run.ProjectId, ct);
        EnsureTenant(run, scope.OrganizationId);
        if (run.Status != AutomationRunStates.DeadLetter)
        {
            throw new ConflictException(
                "AUTOMATION_RUN_NOT_DEAD_LETTERED",
                "Only dead-lettered automation runs can be replayed.");
        }

        run.Status = AutomationRunStates.RetryScheduled;
        run.Outcome = "ReplayScheduled";
        run.FailureCategory = null;
        run.Attempt = 0;
        run.CompletedAtUtc = null;
        run.NextAttemptAtUtc = clock.UtcNow;
        foreach (var step in run.Steps.Where(step => step.Status == AutomationStepStates.Failed))
        {
            step.Status = AutomationStepStates.Pending;
            step.FailureCategory = null;
            step.CompletedAtUtc = null;
        }
        await ReplaceRunAsync(run, useRequestVersion: true, ct);
        await audit.WriteAsync(
            "AutomationRunReplayed",
            run.RuleId,
            run.ProjectId,
            "DeadLetter",
            "RetryScheduled",
            correlationId,
            ct);
        return ToResponse(run);
    }

    public async Task<AutomationRunResponse> GetAsync(string runId, CancellationToken ct)
    {
        var run = await GetRunAsync(runId, ct);
        var scope = await access.EnsureCanViewAsync(run.ProjectId, ct);
        EnsureTenant(run, scope.OrganizationId);
        return ToResponse(run);
    }

    public async Task<AutomationRunPageResponse> ListAsync(
        string projectId,
        string? ruleId,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var scope = await access.EnsureCanViewAsync(projectId, ct);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedRuleId = string.IsNullOrWhiteSpace(ruleId) ? null : ruleId.Trim();
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        var filter = (System.Linq.Expressions.Expression<Func<AutomationRunDocument, bool>>)(run =>
            run.OrganizationId == scope.OrganizationId
            && run.ProjectId == projectId
            && (normalizedRuleId == null || run.RuleId == normalizedRuleId)
            && (normalizedStatus == null || run.Status == normalizedStatus));
        var total = await runs.CountByFilterAsync(filter, ct);
        var documents = await runs.ListByFilterAsync(
            filter,
            run => run.CreatedAtUtc,
            orderDescending: true,
            page,
            pageSize,
            cancellationToken: ct);
        return new AutomationRunPageResponse(
            documents.Select(ToResponse).ToArray(),
            page,
            pageSize,
            total);
    }

    public Task<IReadOnlyList<AutomationRunDocument>> ListDueRetriesAsync(
        int pageSize,
        CancellationToken ct) =>
        runs.ListByFilterAsync(
            run => run.Status == AutomationRunStates.RetryScheduled
                && run.NextAttemptAtUtc <= clock.UtcNow,
            run => run.NextAttemptAtUtc!,
            pageSize: Math.Clamp(pageSize, 1, 200),
            cancellationToken: ct);

    public async Task<IReadOnlyCollection<AutomationScheduleDispatch>> ClaimDueSchedulesAsync(
        int pageSize,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await rules.ListByFilterAsync(
            rule => rule.Active
                && !rule.Archived
                && rule.PublishedVersion > 0
                && rule.NextRunAtUtc <= now
                && (rule.ScheduleClaimedUntilUtc == null
                    || rule.ScheduleClaimedUntilUtc <= now),
            rule => rule.NextRunAtUtc!,
            pageSize: Math.Clamp(pageSize, 1, 200),
            cancellationToken: ct);
        var result = new List<AutomationScheduleDispatch>(due.Count);
        foreach (var candidate in due)
        {
            await using var resourceLock = await AcquireAsync(
                $"automation-rule:{candidate.Id}",
                ct);
            var rule = await rules.SelectAsync(
                current => current.Id == candidate.Id
                    && current.Active
                    && !current.Archived
                    && current.NextRunAtUtc <= now
                    && (current.ScheduleClaimedUntilUtc == null
                        || current.ScheduleClaimedUntilUtc <= now),
                ct);
            if (rule is null)
                continue;

            var definition = CurrentPublished(rule);
            if (definition.Trigger.Type != "Schedule"
                || definition.Trigger.IntervalMinutes is not { } intervalMinutes
                || rule.NextRunAtUtc is not { } scheduledFor)
            {
                continue;
            }

            var claimToken = Guid.NewGuid().ToString("N");
            rule.ScheduleClaimedForUtc = scheduledFor;
            rule.ScheduleClaimedUntilUtc = now.Add(ScheduleClaimDuration);
            rule.ScheduleClaimToken = claimToken;
            rule.UpdatedAt = now;
            var replaced = await rules.ReplaceByVersionAsync(
                current => current.Id == rule.Id,
                rule,
                rule.Version,
                ct);
            if (!replaced.Found)
                continue;

            result.Add(new AutomationScheduleDispatch(
                rule.Id,
                definition.Number,
                definition.Name,
                rule.OrganizationId,
                rule.ProjectId,
                rule.CreatedByUserId,
                scheduledFor,
                claimToken));
        }
        return result;
    }

    public async Task<bool> CompleteScheduleClaimAsync(
        string ruleId,
        DateTimeOffset scheduledForUtc,
        string claimToken,
        CancellationToken ct)
    {
        await using var resourceLock = await AcquireAsync($"automation-rule:{ruleId}", ct);
        var rule = await rules.SelectAsync(
            current => current.Id == ruleId
                && current.Active
                && !current.Archived
                && current.ScheduleClaimedForUtc == scheduledForUtc
                && current.ScheduleClaimToken == claimToken,
            ct);
        if (rule is null)
            return false;

        var definition = CurrentPublished(rule);
        if (definition.Trigger.Type != "Schedule"
            || definition.Trigger.IntervalMinutes is not { } intervalMinutes)
        {
            return false;
        }

        rule.NextRunAtUtc = NextScheduleAfter(scheduledForUtc, intervalMinutes, clock.UtcNow);
        rule.ScheduleClaimedForUtc = null;
        rule.ScheduleClaimedUntilUtc = null;
        rule.ScheduleClaimToken = null;
        rule.UpdatedAt = clock.UtcNow;
        var replaced = await rules.ReplaceByVersionAsync(
            current => current.Id == rule.Id,
            rule,
            rule.Version,
            ct);
        return replaced.Found;
    }

    private async Task<AutomationRunResponse> ExecuteRuleAsync(
        AutomationRuleDocument rule,
        AutomationExecutionContext context,
        CancellationToken ct)
    {
        var definition = CurrentPublished(rule);
        var runId = StableRunId(rule.Id, definition.Number, context.TriggerId, context.SourceId);
        await using var resourceLock = await AcquireAsync($"automation-run:{runId}", ct);
        var existing = await runs.SelectAsync(run => run.Id == runId, ct);
        if (existing is not null)
        {
            return ToResponse(existing);
        }

        var rootRunId = string.IsNullOrWhiteSpace(context.RootRunId)
            ? runId
            : context.RootRunId.Trim();
        var visited = (context.VisitedRuleIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var now = clock.UtcNow;
        var run = new AutomationRunDocument
        {
            Id = runId,
            OrganizationId = context.OrganizationId,
            ProjectId = context.ProjectId,
            RuleId = rule.Id,
            RuleVersion = definition.Number,
            RuleName = definition.Name,
            TriggerType = context.TriggerType,
            EventType = context.EventType,
            TriggerId = context.TriggerId,
            SourceId = context.SourceId,
            ActorUserId = context.ActorUserId,
            RootRunId = rootRunId,
            ChainDepth = context.ChainDepth,
            VisitedRuleIds = visited,
            Fields = context.Fields.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            CorrelationId = context.CorrelationId,
            CreatedAtUtc = now,
            MaximumAttempts = MaximumRunAttempts,
            Steps = definition.Actions.Select((action, index) => new AutomationRunStepDocument
            {
                Index = index,
                ActionType = action.Type
            }).ToList()
        };

        try
        {
            run = await runs.CreateAsync(run, ct);
        }
        catch (DocumentConflictException)
        {
            return ToResponse(await GetRunAsync(runId, ct));
        }

        if (!context.ActorAvailable)
            return await SkipAsync(run, "ActorUnavailable", ct);
        if (visited.Contains(rule.Id, StringComparer.Ordinal))
            return await SkipAsync(run, "LoopPrevented", ct);
        if (context.ChainDepth >= definition.MaximumChainDepth)
            return await SkipAsync(run, "ChainDepthExceeded", ct);
        if (!TriggerMatches(definition.Trigger, context))
            return await SkipAsync(run, "TriggerMismatch", ct);
        if (definition.Condition is not null && !Evaluate(definition.Condition, context.Fields))
            return await SkipAsync(run, "ConditionMismatch", ct);

        var recentExecutions = await runs.CountByFilterAsync(
            candidate => candidate.RuleId == rule.Id
                && candidate.OrganizationId == context.OrganizationId
                && candidate.CreatedAtUtc >= now.AddHours(-1)
                && candidate.Status != AutomationRunStates.Skipped,
            ct);
        if (recentExecutions > definition.MaximumExecutionsPerHour)
            return await SkipAsync(run, "HourlyLimitExceeded", ct);

        run.VisitedRuleIds.Add(rule.Id);
        return await ExecuteActionsAsync(run, definition, ct);
    }

    private async Task<AutomationRunResponse> ExecuteActionsAsync(
        AutomationRunDocument run,
        AutomationRuleVersionDocument definition,
        CancellationToken ct)
    {
        run.Attempt++;
        run.Status = AutomationRunStates.Running;
        run.Outcome = AutomationRunStates.Running;
        run.StartedAtUtc ??= clock.UtcNow;
        run.CompletedAtUtc = null;
        run.NextAttemptAtUtc = null;
        run.FailureCategory = null;
        await ReplaceRunAsync(run, useRequestVersion: false, ct);

        foreach (var step in run.Steps.OrderBy(step => step.Index))
        {
            if (step.Status == AutomationStepStates.Succeeded)
                continue;

            step.Status = AutomationStepStates.Running;
            step.Attempt++;
            step.StartedAtUtc = clock.UtcNow;
            step.CompletedAtUtc = null;
            step.FailureCategory = null;
            await ReplaceRunAsync(run, useRequestVersion: false, ct);

            try
            {
                await actions.ExecuteAsync(
                    new AutomationActionExecution(
                        run.Id,
                        run.RuleId,
                        run.RuleVersion,
                        run.ProjectId,
                        run.SourceId,
                        run.ActorUserId,
                        run.RootRunId,
                        run.ChainDepth + 1,
                        run.VisitedRuleIds,
                        step.Index,
                        ToDefinition(definition.Actions[step.Index]),
                        run.CorrelationId),
                    ct);
                step.Status = AutomationStepStates.Succeeded;
                step.CompletedAtUtc = clock.UtcNow;
                await ReplaceRunAsync(run, useRequestVersion: false, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var category = FailureCategory(exception);
                step.Status = AutomationStepStates.Failed;
                step.FailureCategory = category;
                step.CompletedAtUtc = clock.UtcNow;
                run.FailureCategory = category;
                if (run.Attempt >= run.MaximumAttempts)
                {
                    run.Status = AutomationRunStates.DeadLetter;
                    run.Outcome = AutomationRunStates.DeadLetter;
                    run.CompletedAtUtc = clock.UtcNow;
                    run.NextAttemptAtUtc = null;
                }
                else
                {
                    run.Status = AutomationRunStates.RetryScheduled;
                    run.Outcome = AutomationRunStates.RetryScheduled;
                    run.NextAttemptAtUtc = clock.UtcNow.Add(RetryDelay(run.Attempt));
                }
                await ReplaceRunAsync(run, useRequestVersion: false, ct);
                return ToResponse(run);
            }
        }

        run.Status = AutomationRunStates.Succeeded;
        run.Outcome = AutomationRunStates.Succeeded;
        run.FailureCategory = null;
        run.CompletedAtUtc = clock.UtcNow;
        run.NextAttemptAtUtc = null;
        await ReplaceRunAsync(run, useRequestVersion: false, ct);
        return ToResponse(run);
    }

    private async Task<AutomationRunResponse> SkipAsync(
        AutomationRunDocument run,
        string outcome,
        CancellationToken ct)
    {
        run.Status = AutomationRunStates.Skipped;
        run.Outcome = outcome;
        run.CompletedAtUtc = clock.UtcNow;
        run.NextAttemptAtUtc = null;
        await ReplaceRunAsync(run, useRequestVersion: false, ct);
        return ToResponse(run);
    }

    private async Task<List<AutomationRuleDocument>> ListMatchingRulesAsync(
        AutomationExecutionContext context,
        CancellationToken ct)
    {
        var result = new List<AutomationRuleDocument>();
        string? cursor = null;
        do
        {
            var page = await rules.ListByCursorAsync(
                rule => rule.OrganizationId == context.OrganizationId
                    && rule.ProjectId == context.ProjectId
                    && rule.Active
                    && !rule.Archived
                    && rule.PublishedVersion > 0
                    && (context.RuleId == null || rule.Id == context.RuleId),
                cursor,
                200,
                ct);
            result.AddRange(page.Items.Where(rule =>
                TriggerMatches(CurrentPublished(rule).Trigger, context)));
            cursor = page.NextCursor;
        } while (cursor is not null);
        return result;
    }

    private async Task<AutomationRunDocument> GetRunAsync(string runId, CancellationToken ct) =>
        await runs.SelectAsync(run => run.Id == runId, ct)
        ?? throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");

    private async Task ReplaceRunAsync(
        AutomationRunDocument run,
        bool useRequestVersion,
        CancellationToken ct)
    {
        var expected = useRequestVersion
            ? expectedVersion.Consume(run.Version)
            : run.Version;
        var result = await runs.ReplaceByVersionAsync(
            candidate => candidate.Id == run.Id,
            run,
            expected,
            ct);
        if (!result.Found)
            throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
        run.Version = result.Version!.Value;
    }

    private async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException(
                "AUTOMATION_RESOURCE_BUSY",
                "Automation is busy; retry the operation.");
    }

    private static void ValidateContext(AutomationExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.OrganizationId)
            || string.IsNullOrWhiteSpace(context.ProjectId)
            || string.IsNullOrWhiteSpace(context.TriggerType)
            || string.IsNullOrWhiteSpace(context.TriggerId)
            || string.IsNullOrWhiteSpace(context.SourceId)
            || string.IsNullOrWhiteSpace(context.ActorUserId)
            || string.IsNullOrWhiteSpace(context.CorrelationId)
            || context.Fields is null)
        {
            throw new ValidationException("Automation execution context is incomplete.");
        }
        if (context.ChainDepth is < 0 or > 10)
            throw new ValidationException("Automation execution chain depth is invalid.");
        if ((context.VisitedRuleIds?.Count ?? 0) > 10)
            throw new ValidationException("Automation execution visited-rule list is invalid.");
        if (context.Fields.Count > AutomationRuleDefinitionFactory.MaximumConditionNodes
            || context.Fields.Any(field =>
                string.IsNullOrWhiteSpace(field.Key)
                || field.Key.Length > 50
                || field.Value?.Length > 2000))
        {
            throw new ValidationException("Automation execution fields exceed the supported bounds.");
        }
    }

    private static AutomationRuleVersionDocument CurrentPublished(AutomationRuleDocument rule) =>
        rule.PublishedVersions.Single(version => version.Number == rule.PublishedVersion);

    private static bool TriggerMatches(
        AutomationTriggerDocument trigger,
        AutomationExecutionContext context) =>
        trigger.Type.Equals(context.TriggerType, StringComparison.OrdinalIgnoreCase)
        && (trigger.Type == "Schedule"
            || trigger.EventType!.Equals(context.EventType, StringComparison.OrdinalIgnoreCase));

    private static bool Evaluate(
        AutomationConditionDocument condition,
        IReadOnlyDictionary<string, string?> fields)
    {
        if (condition.Kind == "All")
            return condition.Children.All(child => Evaluate(child, fields));
        if (condition.Kind == "Any")
            return condition.Children.Any(child => Evaluate(child, fields));

        var actual = fields.FirstOrDefault(pair =>
            pair.Key.Equals(condition.Field, StringComparison.OrdinalIgnoreCase)).Value;
        return condition.Operator switch
        {
            "Equals" => string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
            "NotEquals" => !string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
            "Contains" => actual?.Contains(condition.Value!, StringComparison.OrdinalIgnoreCase) == true,
            "NotContains" => actual?.Contains(condition.Value!, StringComparison.OrdinalIgnoreCase) != true,
            "IsEmpty" => string.IsNullOrWhiteSpace(actual),
            "IsNotEmpty" => !string.IsNullOrWhiteSpace(actual),
            _ => false
        };
    }

    private static string StableRunId(
        string ruleId,
        int ruleVersion,
        string triggerId,
        string sourceId)
    {
        var canonical = $"{ruleId}\n{ruleVersion}\n{triggerId}\n{sourceId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromMinutes(Math.Min(Math.Pow(2, Math.Max(attempt - 1, 0)), 15));

    private static DateTimeOffset NextScheduleAfter(
        DateTimeOffset scheduledFor,
        int intervalMinutes,
        DateTimeOffset now)
    {
        var elapsedMinutes = Math.Max(0, (now - scheduledFor).TotalMinutes);
        var intervals = Math.Floor(elapsedMinutes / intervalMinutes) + 1;
        return scheduledFor.AddMinutes(intervals * intervalMinutes);
    }

    private static string FailureCategory(Exception exception) =>
        exception switch
        {
            UnauthorizedException => "AuthenticationUnavailable",
            ForbiddenException => "AuthorizationDenied",
            ValidationException => "ValidationFailed",
            NotFoundException => "ResourceUnavailable",
            ConflictException => "Conflict",
            DocumentConcurrencyException => "Concurrency",
            TimeoutException => "TransientDependency",
            _ => "Unexpected"
        };

    private static AutomationActionDefinition ToDefinition(AutomationActionDocument action) =>
        new(action.Type, action.Value);

    private static void EnsureTenant(AutomationRunDocument run, string organizationId)
    {
        if (!run.OrganizationId.Equals(organizationId, StringComparison.Ordinal))
            throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
    }

    private static AutomationRunResponse ToResponse(AutomationRunDocument run) =>
        new(
            run.Id,
            run.ProjectId,
            run.RuleId,
            run.RuleVersion,
            run.RuleName,
            run.TriggerType,
            run.EventType,
            run.SourceId,
            run.ActorUserId,
            run.RootRunId,
            run.ChainDepth,
            run.Status,
            run.Outcome,
            run.Attempt,
            run.MaximumAttempts,
            run.FailureCategory,
            run.CreatedAtUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.NextAttemptAtUtc,
            run.Steps.OrderBy(step => step.Index).Select(step => new AutomationRunStepResponse(
                step.Index,
                step.ActionType,
                step.Status,
                step.Attempt,
                step.FailureCategory,
                step.StartedAtUtc,
                step.CompletedAtUtc)).ToArray(),
            run.Version);
}
