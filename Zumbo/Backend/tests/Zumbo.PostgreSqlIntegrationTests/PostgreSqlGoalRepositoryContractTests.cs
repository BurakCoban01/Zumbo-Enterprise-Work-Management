using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlGoalRepositoryContractTests(PostgreSqlFixture fixture)
    : GoalRepositoryContract
{
    [Fact]
    public async Task Migration33CreatesGoalTableAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'projects'
              AND table_name = 'goals';
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'projects'
              AND indexname IN (
                'ix_goals_tenant_owner_state',
                'ix_goals_tenant_viewers',
                'ix_goals_tenant_key_results');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Equal(1, tables);
        Assert.Equal(3, indexes);
        Assert.Contains("33:goals", applied);
    }

    protected override IDocumentRepository<GoalDocument> Goals() =>
        fixture.Api.CreateRepository<GoalDocument>("projects", "goals");
}
