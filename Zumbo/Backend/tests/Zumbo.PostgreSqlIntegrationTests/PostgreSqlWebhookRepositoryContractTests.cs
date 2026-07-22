using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlWebhookRepositoryContractTests(PostgreSqlFixture fixture)
    : WebhookRepositoryContract
{
    [Fact]
    public async Task Migration27_creates_webhook_tables_and_indexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'work_items'
              AND table_name IN ('webhook_subscriptions', 'webhook_deliveries');
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'work_items'
              AND indexname IN (
                'ix_webhook_subscriptions_tenant_active',
                'ix_webhook_deliveries_claim',
                'ix_webhook_deliveries_tenant_subscription',
                'ix_webhook_deliveries_tenant_status');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Equal(2, tables);
        Assert.Equal(4, indexes);
        Assert.Contains("27:webhook_subscriptions_and_deliveries", applied);
    }

    protected override IDocumentRepository<WebhookSubscriptionDocument> Subscriptions() =>
        fixture.Api.CreateRepository<WebhookSubscriptionDocument>("work_items", "webhook_subscriptions");
    protected override IDocumentRepository<WebhookDeliveryDocument> Deliveries() =>
        fixture.Api.CreateRepository<WebhookDeliveryDocument>("work_items", "webhook_deliveries");
}
