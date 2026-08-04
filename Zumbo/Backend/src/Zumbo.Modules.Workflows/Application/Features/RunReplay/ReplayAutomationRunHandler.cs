using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows.Application.Mapping.AutomationRuns;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows.Application.Features.RunReplay;

public sealed class ReplayAutomationRunHandler(
    IDocumentRepository<AutomationRunDocument> runs,
    IAutomationProjectAccessChecker access,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    IAutomationAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<AutomationRunResponse> HandleAsync(
        ReplayAutomationRunCommand command,
        CancellationToken ct)
    {
        await using var resourceLock = await AcquireAsync($"automation-run:{command.RunId}", ct);
        var run = await GetRunAsync(command.RunId, ct);
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

        await ReplaceRunAsync(run, ct);
        await audit.WriteAsync(
            "AutomationRunReplayed",
            run.RuleId,
            run.ProjectId,
            "DeadLetter",
            "RetryScheduled",
            command.CorrelationId,
            ct);
        return AutomationRunResponseMapper.ToResponse(run);
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

    private async Task<AutomationRunDocument> GetRunAsync(string runId, CancellationToken ct) =>
        await runs.SelectAsync(run => run.Id == runId, ct)
        ?? throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");

    private static void EnsureTenant(AutomationRunDocument run, string organizationId)
    {
        if (!run.OrganizationId.Equals(organizationId, StringComparison.Ordinal))
        {
            throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
        }
    }

    private async Task ReplaceRunAsync(AutomationRunDocument run, CancellationToken ct)
    {
        var result = await runs.ReplaceByVersionAsync(
            candidate => candidate.Id == run.Id,
            run,
            expectedVersion.Consume(run.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
        }

        run.Version = result.Version!.Value;
    }
}
