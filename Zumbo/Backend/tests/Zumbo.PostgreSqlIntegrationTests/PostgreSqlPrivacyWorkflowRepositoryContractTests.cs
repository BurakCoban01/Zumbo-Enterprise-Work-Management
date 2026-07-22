using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlPrivacyWorkflowRepositoryContractTests(PostgreSqlFixture fixture)
    : PrivacyWorkflowRepositoryContract
{
    [Fact]
    public async Task Migration25_CreatesPrivacyWorkflowTableAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'identity' AND table_name = 'privacy_workflows';
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'identity'
              AND indexname IN (
                'ix_privacy_workflows_owner_state',
                'ix_privacy_workflows_retention',
                'ix_privacy_workflows_retention_utc');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Equal(1, tables);
        Assert.Equal(3, indexes);
        Assert.Contains("25:privacy_workflows", applied);
        Assert.Contains("26:privacy_workflow_utc_index", applied);
    }

    protected override IDocumentRepository<PrivacyWorkflowDocument> Jobs() =>
        fixture.Api.CreateRepository<PrivacyWorkflowDocument>("identity", "privacy_workflows");
}
