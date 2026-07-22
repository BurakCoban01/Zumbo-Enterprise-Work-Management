using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlWorkItemBulkJobRepositoryContractTests(PostgreSqlFixture fixture)
    : WorkItemBulkJobRepositoryContract
{
    [Fact]
    public async Task Migration20_CreatesBulkJobTablesAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'work_items'
              AND table_name IN ('work_item_bulk_jobs', 'work_item_bulk_job_items');
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'work_items'
              AND indexname IN (
                'ux_workitem_bulk_jobs_idempotency',
                'ix_workitem_bulk_jobs_owner_created',
                'ix_workitem_bulk_jobs_state_updated',
                'ux_workitem_bulk_job_items_order',
                'ix_workitem_bulk_job_items_state_order');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Equal(2, tables);
        Assert.Equal(5, indexes);
        Assert.Contains("20:work_item_bulk_jobs", applied);
    }

    protected override IDocumentRepository<WorkItemBulkJobDocument> Jobs() =>
        fixture.Api.CreateRepository<WorkItemBulkJobDocument>("work_items", "work_item_bulk_jobs");
    protected override IDocumentRepository<WorkItemBulkJobItemDocument> Items() =>
        fixture.Api.CreateRepository<WorkItemBulkJobItemDocument>("work_items", "work_item_bulk_job_items");
}
