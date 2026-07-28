using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlDevelopmentIntegrationRepositoryContractTests(
    PostgreSqlFixture fixture)
    : DevelopmentIntegrationRepositoryContract
{
    [Fact]
    public async Task Migration36CreatesDevelopmentIntegrationTablesAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(
            CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(
            connection,
            """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'work_items'
              AND table_name IN (
                'development_connections',
                'development_repository_mappings',
                'work_item_development_links',
                'development_webhook_receipts');
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(
            connection,
            """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'work_items'
              AND indexname IN (
                'ix_development_connections_tenant_updated',
                'ux_development_mappings_tenant_connection_repository',
                'ix_development_mappings_tenant_project_active',
                'ix_development_links_tenant_work_item_updated',
                'ix_development_links_tenant_mapping_commit',
                'ix_development_receipts_connection_expiry');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(
            CancellationToken.None);

        Assert.Equal(4, tables);
        Assert.Equal(6, indexes);
        Assert.Contains("36:development_integrations", applied);
    }

    protected override IDocumentRepository<DevelopmentConnectionDocument>
        Connections() =>
        fixture.Api.CreateRepository<DevelopmentConnectionDocument>(
            "work_items",
            "development_connections");

    protected override IDocumentRepository<DevelopmentRepositoryMappingDocument>
        Mappings() =>
        fixture.Api.CreateRepository<DevelopmentRepositoryMappingDocument>(
            "work_items",
            "development_repository_mappings");

    protected override IDocumentRepository<WorkItemDevelopmentLinkDocument>
        Links() =>
        fixture.Api.CreateRepository<WorkItemDevelopmentLinkDocument>(
            "work_items",
            "work_item_development_links");

    protected override IDocumentRepository<DevelopmentWebhookReceiptDocument>
        Receipts() =>
        fixture.Api.CreateRepository<DevelopmentWebhookReceiptDocument>(
            "work_items",
            "development_webhook_receipts");
}
