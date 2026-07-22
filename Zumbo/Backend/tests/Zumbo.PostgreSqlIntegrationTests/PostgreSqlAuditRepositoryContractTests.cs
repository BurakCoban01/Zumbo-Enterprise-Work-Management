using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlAuditRepositoryContractTests(PostgreSqlFixture fixture) : AuditRepositoryContract
{
    [Fact]
    public async Task Migration21_CreatesTenantAndChainIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'audit'
              AND indexname IN (
                'ix_audit_logs_organization_created',
                'ix_audit_logs_organization_entity_created',
                'ix_audit_logs_organization_actor_created',
                'ux_audit_logs_organization_chain_sequence');
            """);
        Assert.Equal(4, indexes);
        Assert.Contains("21:audit_tenant_indexes", await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None));
    }

    protected override IDocumentRepository<AuditLogDocument> Repository() =>
        fixture.Api.CreateRepository<AuditLogDocument>("audit", "audit_logs");
}
