using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlAutomationRepositoryContractTests(PostgreSqlFixture fixture)
    : AutomationRepositoryContract
{
    [Fact]
    public async Task Migration29_CreatesAutomationRuleTableAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'workflows'
              AND table_name = 'automation_rules';
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'workflows'
              AND indexname IN (
                'ix_automation_rules_tenant_project_state',
                'ix_automation_rules_schedule');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Equal(1, tables);
        Assert.Equal(2, indexes);
        Assert.Contains("29:automation_rules", applied);
    }

    protected override IDocumentRepository<AutomationRuleDocument> Rules() =>
        fixture.Api.CreateRepository<AutomationRuleDocument>("workflows", "automation_rules");
}
