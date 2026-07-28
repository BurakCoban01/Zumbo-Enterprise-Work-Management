using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlDashboardRepositoryContractTests(PostgreSqlFixture fixture)
    : DashboardRepositoryContract
{
    [Fact]
    public async Task Migration31CreatesDashboardTableAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'work_items'
              AND table_name = 'dashboards';
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'work_items'
              AND indexname IN (
                'ix_dashboards_tenant_owner_state',
                'ix_dashboards_tenant_viewers',
                'ix_dashboards_tenant_projects');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Equal(1, tables);
        Assert.Equal(3, indexes);
        Assert.Contains("31:dashboards", applied);
    }

    protected override IDocumentRepository<DashboardDocument> Dashboards() =>
        fixture.Api.CreateRepository<DashboardDocument>("work_items", "dashboards");
}
