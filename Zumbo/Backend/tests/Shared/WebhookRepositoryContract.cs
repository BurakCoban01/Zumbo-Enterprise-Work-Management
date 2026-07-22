using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.RepositoryContracts;

public abstract class WebhookRepositoryContract
{
    protected abstract IDocumentRepository<WebhookSubscriptionDocument> Subscriptions();
    protected abstract IDocumentRepository<WebhookDeliveryDocument> Deliveries();

    [Fact]
    public async Task Webhook_store_preserves_tenant_cas_deduplication_and_atomic_claim()
    {
        var subscriptions = Subscriptions();
        var deliveries = Deliveries();
        var prefix = "platform007-contract-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var owned = new WebhookSubscriptionDocument
        {
            Id = prefix + "-subscription",
            OrganizationId = prefix + "-tenant",
            Name = "Contract",
            TargetUrl = "https://receiver.example.test/events",
            EventScopes = ["work-item.created"],
            CurrentSecretProtected = "protected",
            CurrentSecretFingerprint = "fingerprint",
            CreatedByUserId = prefix + "-user",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var foreign = new WebhookSubscriptionDocument
        {
            Id = prefix + "-foreign-subscription",
            OrganizationId = prefix + "-foreign-tenant",
            Name = "Foreign",
            TargetUrl = "https://receiver.example.test/events",
            EventScopes = ["work-item.created"],
            CurrentSecretProtected = "protected",
            CurrentSecretFingerprint = "fingerprint",
            CreatedByUserId = prefix + "-foreign-user",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var delivery = new WebhookDeliveryDocument
        {
            Id = prefix + "-delivery",
            OrganizationId = owned.OrganizationId,
            SubscriptionId = owned.Id,
            SourceEventId = prefix + "-event",
            EventScope = "work-item.created",
            TargetUrl = owned.TargetUrl,
            Payload = "{\"schemaVersion\":1}",
            PayloadSha256 = new string('a', 64),
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        try
        {
            owned = await subscriptions.CreateAsync(owned);
            await subscriptions.CreateAsync(foreign);
            var stale = await subscriptions.SelectAsync(x => x.Id == owned.Id);
            owned.Name = "Contract updated";
            var replaced = await subscriptions.ReplaceByVersionAsync(
                x => x.Id == owned.Id && x.OrganizationId == owned.OrganizationId,
                owned,
                owned.Version);
            Assert.True(replaced.Found);
            stale!.Name = "Stale";
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                subscriptions.ReplaceByVersionAsync(x => x.Id == stale.Id, stale, stale.Version));
            Assert.Equal(owned.Id, Assert.Single((await subscriptions.ListByCursorAsync(
                x => x.OrganizationId == owned.OrganizationId)).Items).Id);

            await deliveries.CreateAsync(delivery);
            await Assert.ThrowsAsync<DocumentConflictException>(() => deliveries.CreateAsync(delivery));
            var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
            {
                var candidate = await deliveries.SelectAsync(x => x.Id == delivery.Id);
                candidate!.Status = WebhookDeliveryStatuses.Processing;
                candidate.LeaseToken = "lease-" + index;
                candidate.ClaimedBy = "worker-" + index;
                candidate.LeaseUntilUtc = now.AddMinutes(1);
                return await deliveries.ReplaceByFilterAsync(
                    x => x.Id == delivery.Id
                        && x.OrganizationId == owned.OrganizationId
                        && x.Status == WebhookDeliveryStatuses.Pending
                        && x.NextAttemptAtUtc <= now,
                    candidate);
            }));
            Assert.Equal(1, attempts.Count(x => x.MatchedCount == 1));
            var claimed = await deliveries.SelectAsync(x => x.Id == delivery.Id);
            Assert.Equal(WebhookDeliveryStatuses.Processing, claimed!.Status);
            Assert.StartsWith("lease-", claimed.LeaseToken);
        }
        finally
        {
            await deliveries.DeleteByFilterAsync(x => x.Id == delivery.Id);
            await subscriptions.DeleteByFilterAsync(x => x.Id == owned.Id || x.Id == foreign.Id);
        }
    }
}
