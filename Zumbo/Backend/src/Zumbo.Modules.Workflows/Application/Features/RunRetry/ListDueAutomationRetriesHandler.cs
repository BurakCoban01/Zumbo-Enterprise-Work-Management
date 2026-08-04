using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows.Application.Features.RunRetry;

public sealed class ListDueAutomationRetriesHandler(
    IDocumentRepository<AutomationRunDocument> runs,
    IClock clock)
{
    public async Task<IReadOnlyList<DueAutomationRetry>> HandleAsync(
        ListDueAutomationRetriesQuery query,
        CancellationToken ct)
    {
        var documents = await runs.ListByFilterAsync(
            run => run.Status == AutomationRunStates.RetryScheduled
                && run.NextAttemptAtUtc <= clock.UtcNow,
            run => run.NextAttemptAtUtc!,
            pageSize: Math.Clamp(query.PageSize, 1, 200),
            cancellationToken: ct);

        return documents.Select(run => new DueAutomationRetry(
            run.Id,
            run.OrganizationId,
            run.ActorUserId,
            run.CorrelationId)).ToArray();
    }
}
