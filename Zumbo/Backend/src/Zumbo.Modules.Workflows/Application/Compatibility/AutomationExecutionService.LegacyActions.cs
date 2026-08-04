using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService
{
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

    private static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromMinutes(Math.Min(Math.Pow(2, Math.Max(attempt - 1, 0)), 15));

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
}
