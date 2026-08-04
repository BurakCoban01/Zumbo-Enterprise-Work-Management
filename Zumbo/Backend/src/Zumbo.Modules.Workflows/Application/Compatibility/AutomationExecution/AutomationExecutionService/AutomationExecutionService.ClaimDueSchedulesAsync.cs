using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    public async Task<IReadOnlyCollection<AutomationScheduleDispatch>> ClaimDueSchedulesAsync(
        int pageSize,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await rules.ListByFilterAsync(
            rule => rule.Active
                && !rule.Archived
                && rule.PublishedVersion > 0
                && rule.NextRunAtUtc <= now
                && (rule.ScheduleClaimedUntilUtc == null
                    || rule.ScheduleClaimedUntilUtc <= now),
            rule => rule.NextRunAtUtc!,
            pageSize: Math.Clamp(pageSize, 1, 200),
            cancellationToken: ct);
        var result = new List<AutomationScheduleDispatch>(due.Count);
        foreach (var candidate in due)
        {
            await using var resourceLock = await AcquireAsync(
                $"automation-rule:{candidate.Id}",
                ct);
            var rule = await rules.SelectAsync(
                current => current.Id == candidate.Id
                    && current.Active
                    && !current.Archived
                    && current.NextRunAtUtc <= now
                    && (current.ScheduleClaimedUntilUtc == null
                        || current.ScheduleClaimedUntilUtc <= now),
                ct);
            if (rule is null)
                continue;

            var definition = CurrentPublished(rule);
            if (definition.Trigger.Type != "Schedule"
                || definition.Trigger.IntervalMinutes is not { } intervalMinutes
                || rule.NextRunAtUtc is not { } scheduledFor)
            {
                continue;
            }

            var claimToken = Guid.NewGuid().ToString("N");
            rule.ScheduleClaimedForUtc = scheduledFor;
            rule.ScheduleClaimedUntilUtc = now.Add(ScheduleClaimDuration);
            rule.ScheduleClaimToken = claimToken;
            rule.UpdatedAt = now;
            var replaced = await rules.ReplaceByVersionAsync(
                current => current.Id == rule.Id,
                rule,
                rule.Version,
                ct);
            if (!replaced.Found)
                continue;

            result.Add(new AutomationScheduleDispatch(
                rule.Id,
                definition.Number,
                definition.Name,
                rule.OrganizationId,
                rule.ProjectId,
                rule.CreatedByUserId,
                scheduledFor,
                claimToken));
        }
        return result;
    }
}
