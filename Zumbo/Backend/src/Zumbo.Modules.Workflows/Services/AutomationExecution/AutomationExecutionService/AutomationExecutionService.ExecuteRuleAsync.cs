using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

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
}
