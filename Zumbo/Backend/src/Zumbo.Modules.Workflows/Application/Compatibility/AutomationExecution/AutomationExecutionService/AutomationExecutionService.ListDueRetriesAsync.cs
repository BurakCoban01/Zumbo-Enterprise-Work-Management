using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    public Task<IReadOnlyList<AutomationRunDocument>> ListDueRetriesAsync(
        int pageSize,
        CancellationToken ct) =>
        runs.ListByFilterAsync(
            run => run.Status == AutomationRunStates.RetryScheduled
                && run.NextAttemptAtUtc <= clock.UtcNow,
            run => run.NextAttemptAtUtc!,
            pageSize: Math.Clamp(pageSize, 1, 200),
            cancellationToken: ct);
}
