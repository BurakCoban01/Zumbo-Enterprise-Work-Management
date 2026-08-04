using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows.Application.Features.ActionExecution;
using Zumbo.Modules.Workflows.Application.Mapping.AutomationRuns;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows.Application.Features.RunResume;

public sealed class ResumeAutomationRunHandler(
    IDocumentRepository<AutomationRuleDocument> rules,
    IDocumentRepository<AutomationRunDocument> runs,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    AutomationRunActionExecutor actionExecutor)
{
    public async Task<AutomationRunResponse> HandleAsync(
        ResumeAutomationRunCommand command,
        CancellationToken ct)
    {
        await using var resourceLock = await AcquireAsync($"automation-run:{command.RunId}", ct);
        var run = await runs.SelectAsync(candidate => candidate.Id == command.RunId, ct)
            ?? throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
        if (run.Status != AutomationRunStates.RetryScheduled)
        {
            return AutomationRunResponseMapper.ToResponse(run);
        }

        if (run.NextAttemptAtUtc is { } next && next > clock.UtcNow)
        {
            return AutomationRunResponseMapper.ToResponse(run);
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

        if (!command.ActorAvailable)
        {
            return await SkipAsync(run, "ActorUnavailable", ct);
        }

        return await actionExecutor.ExecuteAsync(run, definition, ct);
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

    private async Task<AutomationRunResponse> SkipAsync(
        AutomationRunDocument run,
        string outcome,
        CancellationToken ct)
    {
        run.Status = AutomationRunStates.Skipped;
        run.Outcome = outcome;
        run.CompletedAtUtc = clock.UtcNow;
        run.NextAttemptAtUtc = null;
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
        return AutomationRunResponseMapper.ToResponse(run);
    }
}
