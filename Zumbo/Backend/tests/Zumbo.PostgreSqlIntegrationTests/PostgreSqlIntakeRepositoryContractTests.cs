using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlIntakeRepositoryContractTests(PostgreSqlFixture fixture)
    : IntakeRepositoryContract
{
    [Fact]
    public async Task Migration28_CreatesIntakeTablesAndIndexes()
    {
        await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
        var tables = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'work_items'
              AND table_name IN ('intake_forms', 'intake_form_versions', 'intake_submissions');
            """);
        var indexes = await PostgreSqlFixture.ScalarAsync<long>(connection, """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'work_items'
              AND indexname IN (
                'ux_intake_forms_public_id',
                'ix_intake_forms_tenant_project_state',
                'ux_intake_form_versions_number',
                'ux_intake_submissions_idempotency',
                'ix_intake_submissions_triage',
                'ux_intake_submissions_work_item');
            """);
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        Assert.Equal(3, tables);
        Assert.Equal(6, indexes);
        Assert.Contains("28:intake_forms_and_submissions", applied);
    }

    protected override IDocumentRepository<IntakeFormDocument> Forms() =>
        fixture.Api.CreateRepository<IntakeFormDocument>("work_items", "intake_forms");

    protected override IDocumentRepository<IntakeFormVersionDocument> Versions() =>
        fixture.Api.CreateRepository<IntakeFormVersionDocument>("work_items", "intake_form_versions");

    protected override IDocumentRepository<IntakeSubmissionDocument> Submissions() =>
        fixture.Api.CreateRepository<IntakeSubmissionDocument>("work_items", "intake_submissions");
}
