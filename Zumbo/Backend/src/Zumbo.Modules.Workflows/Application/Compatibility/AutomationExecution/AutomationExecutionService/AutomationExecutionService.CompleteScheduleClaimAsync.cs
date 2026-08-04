using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    public async Task<bool> CompleteScheduleClaimAsync(
        string ruleId,
        DateTimeOffset scheduledForUtc,
        string claimToken,
        CancellationToken ct)
    {
        await using var resourceLock = await AcquireAsync($"automation-rule:{ruleId}", ct);
        var rule = await rules.SelectAsync(
            current => current.Id == ruleId
                && current.Active
                && !current.Archived
                && current.ScheduleClaimedForUtc == scheduledForUtc
                && current.ScheduleClaimToken == claimToken,
            ct);
        if (rule is null)
            return false;

        var definition = CurrentPublished(rule);
        if (definition.Trigger.Type != "Schedule"
            || definition.Trigger.IntervalMinutes is not { } intervalMinutes)
        {
            return false;
        }

        rule.NextRunAtUtc = NextScheduleAfter(scheduledForUtc, intervalMinutes, clock.UtcNow);
        rule.ScheduleClaimedForUtc = null;
        rule.ScheduleClaimedUntilUtc = null;
        rule.ScheduleClaimToken = null;
        rule.UpdatedAt = clock.UtcNow;
        var replaced = await rules.ReplaceByVersionAsync(
            current => current.Id == rule.Id,
            rule,
            rule.Version,
            ct);
        return replaced.Found;
    }
}
