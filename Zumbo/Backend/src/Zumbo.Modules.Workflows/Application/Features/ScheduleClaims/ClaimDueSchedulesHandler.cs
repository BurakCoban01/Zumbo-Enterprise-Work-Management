using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows.Application.Features.ScheduleClaims;

public sealed class ClaimDueSchedulesHandler(
    IDocumentRepository<AutomationRuleDocument> rules,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock)
{
    private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyCollection<AutomationScheduleDispatch>> HandleAsync(
        ClaimDueSchedulesQuery query,
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
            pageSize: Math.Clamp(query.PageSize, 1, 200),
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
            {
                continue;
            }

            var definition = CurrentPublished(rule);
            if (definition.Trigger.Type != "Schedule"
                || definition.Trigger.IntervalMinutes is not { }
                || rule.NextRunAtUtc is not { } scheduledFor)
            {
                continue;
            }

            var claimToken = Guid.NewGuid().ToString("N");
            rule.ScheduleClaimedForUtc = scheduledFor;
            rule.ScheduleClaimedUntilUtc = now.Add(ClaimDuration);
            rule.ScheduleClaimToken = claimToken;
            rule.UpdatedAt = now;
            var replaced = await rules.ReplaceByVersionAsync(
                current => current.Id == rule.Id,
                rule,
                rule.Version,
                ct);
            if (!replaced.Found)
            {
                continue;
            }

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
}
