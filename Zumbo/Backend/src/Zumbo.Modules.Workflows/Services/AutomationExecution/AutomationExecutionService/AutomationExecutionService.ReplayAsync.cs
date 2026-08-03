using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

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
}
