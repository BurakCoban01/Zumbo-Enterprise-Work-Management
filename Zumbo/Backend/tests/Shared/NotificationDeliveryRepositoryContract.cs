using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Notifications;

namespace Zumbo.RepositoryContracts;

public abstract class NotificationDeliveryRepositoryContract
{
    protected abstract IDocumentRepository<NotificationDocument> Repository();

    [Fact]
    public async Task TwoWorkersClaimOnceAndExpiredLeaseCanBeRecovered()
    {
        var repository = Repository();
        var now = new DateTimeOffset(2026, 7, 20, 20, 0, 0, TimeSpan.Zero);
        await repository.CreateAsync(Pending("delivery-1", "org-1", "dedupe-1", now));

        var claims = await Task.WhenAll(
            ClaimAsync(repository, "delivery-1", "worker-a", now),
            ClaimAsync(repository, "delivery-1", "worker-b", now));
        Assert.Equal(1, claims.Count(result => result.MatchedCount == 1));
        var claimed = await repository.SelectAsync(x => x.Id == "delivery-1")
            ?? throw new InvalidOperationException();
        Assert.Equal(NotificationEmailStatuses.Processing, claimed.EmailStatus);
        Assert.Contains(claimed.EmailClaimedBy, new[] { "worker-a", "worker-b" });

        claimed.EmailLeaseUntil = now.AddSeconds(-1);
        await repository.ReplaceByFilterAsync(x => x.Id == claimed.Id, claimed);
        var recovered = await ClaimAsync(repository, claimed.Id, "worker-recovery", now);
        Assert.Equal(1, recovered.MatchedCount);
        Assert.Equal("worker-recovery", (await repository.SelectAsync(x => x.Id == claimed.Id))!.EmailClaimedBy);
    }

    [Fact]
    public async Task TenantScopedDedupeFieldsRoundTripWithoutCrossTenantFiltering()
    {
        var repository = Repository();
        var now = new DateTimeOffset(2026, 7, 20, 20, 0, 0, TimeSpan.Zero);
        await repository.CreateAsync(Pending("delivery-org-1", "org-1", "shared", now));
        await repository.CreateAsync(Pending("delivery-org-2", "org-2", "shared", now));

        Assert.Equal(1, await repository.CountByFilterAsync(
            x => x.OrganizationId == "org-1" && x.DeduplicationKey == "shared"));
        Assert.Equal(1, await repository.CountByFilterAsync(
            x => x.OrganizationId == "org-2" && x.DeduplicationKey == "shared"));
    }

    private static async Task<DocumentMutationResult> ClaimAsync(
        IDocumentRepository<NotificationDocument> repository,
        string id,
        string worker,
        DateTimeOffset now)
    {
        var candidate = await repository.SelectAsync(x => x.Id == id)
            ?? throw new InvalidOperationException();
        candidate.EmailStatus = NotificationEmailStatuses.Processing;
        candidate.EmailClaimedBy = worker;
        candidate.EmailLeaseToken = worker + "-token";
        candidate.EmailLeaseUntil = now.AddMinutes(1);
        return await repository.ReplaceByFilterAsync(
            x => x.Id == id
                && ((x.EmailStatus == NotificationEmailStatuses.Pending && x.EmailNextAttemptAt <= now)
                    || (x.EmailStatus == NotificationEmailStatuses.Processing && x.EmailLeaseUntil <= now)),
            candidate);
    }

    private static NotificationDocument Pending(
        string id,
        string organizationId,
        string deduplicationKey,
        DateTimeOffset now) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        UserId = "user-1",
        Type = "Assignment",
        Message = "Assigned",
        EmailAddress = "user-1@zumbo.local",
        EmailStatus = NotificationEmailStatuses.Pending,
        EmailNextAttemptAt = now,
        DeduplicationKey = deduplicationKey,
        CreatedAt = now
    };
}
