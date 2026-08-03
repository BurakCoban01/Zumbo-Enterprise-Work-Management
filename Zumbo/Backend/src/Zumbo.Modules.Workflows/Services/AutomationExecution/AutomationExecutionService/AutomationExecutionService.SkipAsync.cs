using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

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
}
