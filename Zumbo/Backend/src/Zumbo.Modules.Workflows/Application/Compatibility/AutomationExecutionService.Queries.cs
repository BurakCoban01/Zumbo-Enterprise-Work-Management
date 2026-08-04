using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows.Application.Features.RunQueries;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService
{
    public async Task<AutomationRunResponse> GetAsync(string runId, CancellationToken ct) =>
        await getAutomationRunHandler.HandleAsync(new GetAutomationRunQuery(runId), ct);

    public async Task<AutomationRunPageResponse> ListAsync(
        string projectId,
        string? ruleId,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct) =>
        await listAutomationRunsHandler.HandleAsync(
            new ListAutomationRunsQuery(projectId, ruleId, status, page, pageSize),
            ct);

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
