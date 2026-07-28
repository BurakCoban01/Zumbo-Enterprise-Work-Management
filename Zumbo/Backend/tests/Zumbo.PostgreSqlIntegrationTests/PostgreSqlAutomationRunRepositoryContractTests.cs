using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlAutomationRunRepositoryContractTests(PostgreSqlFixture fixture)
    : AutomationRunRepositoryContract
{
    [Fact]
    public async Task Migration30_CreatesAutomationRunTableAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'workflows'
              AND table_name = 'automation_runs';
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'workflows'
              AND indexname IN (
                'ix_automation_runs_tenant_project_created',
                'ix_automation_runs_rule_created',
                'ix_automation_runs_retry');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Equal(1, tables);
        Assert.Equal(3, indexes);
        Assert.Contains("30:automation_runs", applied);
    }

    protected override IDocumentRepository<AutomationRunDocument> Runs() =>
        fixture.Api.CreateRepository<AutomationRunDocument>("workflows", "automation_runs");
}
