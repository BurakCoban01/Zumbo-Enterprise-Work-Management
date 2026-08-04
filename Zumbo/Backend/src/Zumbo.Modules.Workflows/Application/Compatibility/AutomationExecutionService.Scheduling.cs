using Zumbo.Modules.Workflows.Application.Features.ScheduleClaims;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService
{
    public async Task<IReadOnlyCollection<AutomationScheduleDispatch>> ClaimDueSchedulesAsync(
        int pageSize,
        CancellationToken ct)
        => await claimDueSchedulesHandler.HandleAsync(
            new ClaimDueSchedulesQuery(pageSize),
            ct);

    public async Task<bool> CompleteScheduleClaimAsync(
        string ruleId,
        DateTimeOffset scheduledForUtc,
        string claimToken,
        CancellationToken ct)
        => await completeScheduleClaimHandler.HandleAsync(
            new CompleteScheduleClaimCommand(ruleId, scheduledForUtc, claimToken),
            ct);

    private static DateTimeOffset NextScheduleAfter(
        DateTimeOffset scheduledFor,
        int intervalMinutes,
        DateTimeOffset now)
    {
        var elapsedMinutes = Math.Max(0, (now - scheduledFor).TotalMinutes);
        var intervals = Math.Floor(elapsedMinutes / intervalMinutes) + 1;
        return scheduledFor.AddMinutes(intervals * intervalMinutes);
    }
}
