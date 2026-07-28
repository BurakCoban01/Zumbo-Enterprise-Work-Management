using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlCapacityPlanRepositoryContractTests(
    PostgreSqlFixture fixture) : CapacityPlanRepositoryContract
{
    [Fact]
    public async Task Migration34CreatesCapacityPlanTableAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(
            CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'work_items'
              AND table_name = 'capacity_plans';
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'work_items'
              AND indexname IN (
                'ix_capacity_plans_tenant_owner_state',
                'ix_capacity_plans_tenant_viewers',
                'ix_capacity_plans_tenant_projects');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(
            CancellationToken.None);
        Assert.Equal(1, tables);
        Assert.Equal(3, indexes);
        Assert.Contains("34:capacity_plans", applied);
    }

    protected override IDocumentRepository<CapacityPlanDocument> Plans() =>
        fixture.Api.CreateRepository<CapacityPlanDocument>(
            "work_items",
            "capacity_plans");
}
