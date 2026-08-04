namespace Zumbo.Modules.Workflows.Application.Mapping.AutomationRuns;

public static class AutomationRunResponseMapper
{
    public static AutomationRunResponse ToResponse(AutomationRunDocument run) =>
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
