using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlKnowledgeRepositoryContractTests(
    PostgreSqlFixture fixture) : KnowledgeRepositoryContract
{
    [Fact]
    public async Task Migration35CreatesKnowledgeTableAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(
            CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'projects'
              AND table_name = 'knowledge_documents';
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'projects'
              AND indexname IN (
                'ix_knowledge_tenant_scope_state',
                'ix_knowledge_tenant_owner_state',
                'ix_knowledge_tenant_tags');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(
            CancellationToken.None);
        Assert.Equal(1, tables);
        Assert.Equal(3, indexes);
        Assert.Contains("35:knowledge_documents", applied);
    }

    protected override IDocumentRepository<KnowledgeDocument> Documents() =>
        fixture.Api.CreateRepository<KnowledgeDocument>(
            "projects",
            "knowledge_documents");
}
