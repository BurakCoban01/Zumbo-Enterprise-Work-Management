using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

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
}
