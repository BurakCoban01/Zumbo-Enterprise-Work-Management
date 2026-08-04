using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService
{
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

    private static void EnsureTenant(AutomationRunDocument run, string organizationId)
    {
        if (!run.OrganizationId.Equals(organizationId, StringComparison.Ordinal))
            throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
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
