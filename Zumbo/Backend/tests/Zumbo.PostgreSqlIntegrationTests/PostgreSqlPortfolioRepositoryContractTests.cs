using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlPortfolioRepositoryContractTests(PostgreSqlFixture fixture)
    : PortfolioRepositoryContract
{
    [Fact]
    public async Task Migration32CreatesPortfolioTableAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'projects'
              AND table_name = 'portfolios';
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'projects'
              AND indexname IN (
                'ix_portfolios_tenant_owner_state',
                'ix_portfolios_tenant_viewers',
                'ix_portfolios_tenant_initiatives');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Equal(1, tables);
        Assert.Equal(3, indexes);
        Assert.Contains("32:portfolios", applied);
    }

    protected override IDocumentRepository<PortfolioDocument> Portfolios() =>
        fixture.Api.CreateRepository<PortfolioDocument>("projects", "portfolios");
}
