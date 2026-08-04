using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows.Application.Features.ScheduleClaims;

public sealed class CompleteScheduleClaimHandler(
    IDocumentRepository<AutomationRuleDocument> rules,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock)
{
    public async Task<bool> HandleAsync(
        CompleteScheduleClaimCommand command,
        CancellationToken ct)
    {
        await using var resourceLock = await AcquireAsync(
            $"automation-rule:{command.RuleId}",
            ct);
        var rule = await rules.SelectAsync(
            current => current.Id == command.RuleId
                && current.Active
                && !current.Archived
                && current.ScheduleClaimedForUtc == command.ScheduledForUtc
                && current.ScheduleClaimToken == command.ClaimToken,
            ct);
        if (rule is null)
        {
            return false;
        }

        var definition = CurrentPublished(rule);
        if (definition.Trigger.Type != "Schedule"
            || definition.Trigger.IntervalMinutes is not { } intervalMinutes)
        {
            return false;
        }

        rule.NextRunAtUtc = NextScheduleAfter(
            command.ScheduledForUtc,
            intervalMinutes,
            clock.UtcNow);
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

    private static AutomationRuleVersionDocument CurrentPublished(AutomationRuleDocument rule) =>
        rule.PublishedVersions.SingleOrDefault(
            version => version.Number == rule.PublishedVersion)
        ?? throw new ConflictException(
            "AUTOMATION_PUBLISHED_VERSION_MISSING",
            "The published automation version is unavailable.");

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
