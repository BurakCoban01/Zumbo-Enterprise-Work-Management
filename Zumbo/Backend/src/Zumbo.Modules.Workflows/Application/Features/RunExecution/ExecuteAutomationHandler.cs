using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows.Application.Features.ActionExecution;
using Zumbo.Modules.Workflows.Application.Mapping.AutomationRuns;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows.Application.Features.RunExecution;

public sealed class ExecuteAutomationHandler(
    IDocumentRepository<AutomationRuleDocument> rules,
    IDocumentRepository<AutomationRunDocument> runs,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    AutomationRunActionExecutor actionExecutor)
{
    private const int MaximumRunAttempts = 3;

    public async Task<IReadOnlyCollection<AutomationRunResponse>> HandleAsync(
        ExecuteAutomationCommand command,
        CancellationToken ct)
    {
        var context = command.Context;
        ValidateContext(context);
        var matchingRules = await ListMatchingRulesAsync(context, ct);
        var responses = new List<AutomationRunResponse>(matchingRules.Count);
        foreach (var rule in matchingRules)
        {
            responses.Add(await ExecuteRuleAsync(rule, context, ct));
        }

        return responses;
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
            return AutomationRunResponseMapper.ToResponse(existing);
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
            return AutomationRunResponseMapper.ToResponse(await GetRunAsync(runId, ct));
        }

        if (!context.ActorAvailable)
        {
            return await SkipAsync(run, "ActorUnavailable", ct);
        }
        if (visited.Contains(rule.Id, StringComparer.Ordinal))
        {
            return await SkipAsync(run, "LoopPrevented", ct);
        }
        if (context.ChainDepth >= definition.MaximumChainDepth)
        {
            return await SkipAsync(run, "ChainDepthExceeded", ct);
        }
        if (!TriggerMatches(definition.Trigger, context))
        {
            return await SkipAsync(run, "TriggerMismatch", ct);
        }
        if (definition.Condition is not null && !Evaluate(definition.Condition, context.Fields))
        {
            return await SkipAsync(run, "ConditionMismatch", ct);
        }

        var recentExecutions = await runs.CountByFilterAsync(
            candidate => candidate.RuleId == rule.Id
                && candidate.OrganizationId == context.OrganizationId
                && candidate.CreatedAtUtc >= now.AddHours(-1)
                && candidate.Status != AutomationRunStates.Skipped,
            ct);
        if (recentExecutions > definition.MaximumExecutionsPerHour)
        {
            return await SkipAsync(run, "HourlyLimitExceeded", ct);
        }

        run.VisitedRuleIds.Add(rule.Id);
        return await actionExecutor.ExecuteAsync(run, definition, ct);
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

    private async Task<AutomationRunResponse> SkipAsync(
        AutomationRunDocument run,
        string outcome,
        CancellationToken ct)
    {
        run.Status = AutomationRunStates.Skipped;
        run.Outcome = outcome;
        run.CompletedAtUtc = clock.UtcNow;
        run.NextAttemptAtUtc = null;
        await ReplaceRunAsync(run, ct);
        return AutomationRunResponseMapper.ToResponse(run);
    }

    private async Task ReplaceRunAsync(AutomationRunDocument run, CancellationToken ct)
    {
        var result = await runs.ReplaceByVersionAsync(
            candidate => candidate.Id == run.Id,
            run,
            run.Version,
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
        }

        run.Version = result.Version!.Value;
    }

    private async Task<AutomationRunDocument> GetRunAsync(string runId, CancellationToken ct) =>
        await runs.SelectAsync(run => run.Id == runId, ct)
        ?? throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");

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
        {
            return condition.Children.All(child => Evaluate(child, fields));
        }
        if (condition.Kind == "Any")
        {
            return condition.Children.Any(child => Evaluate(child, fields));
        }

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
        {
            throw new ValidationException("Automation execution chain depth is invalid.");
        }
        if ((context.VisitedRuleIds?.Count ?? 0) > 10)
        {
            throw new ValidationException("Automation execution visited-rule list is invalid.");
        }
        if (context.Fields.Count > AutomationRuleDefinitionFactory.MaximumConditionNodes
            || context.Fields.Any(field =>
                string.IsNullOrWhiteSpace(field.Key)
                || field.Key.Length > 50
                || field.Value?.Length > 2000))
        {
            throw new ValidationException("Automation execution fields exceed the supported bounds.");
        }
    }
}
