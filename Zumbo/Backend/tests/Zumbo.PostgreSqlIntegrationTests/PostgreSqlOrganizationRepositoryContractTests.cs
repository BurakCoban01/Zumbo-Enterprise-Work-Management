using Zumbo.Modules.Organizations;
using Zumbo.Persistence.PostgreSql;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlOrganizationRepositoryContractTests(PostgreSqlFixture fixture)
    : OrganizationRepositoryContract
{
    [Fact]
    public async Task Migration12_BackfillsLegacyOrganizationForCas()
    {
        var id = "legacy-organization-" + Guid.NewGuid().ToString("N");
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        var projectMigration = Assert.Single(
            applied,
            migration => migration.StartsWith("14:", StringComparison.Ordinal));
        await fixture.Api.RollbackAsync(projectMigration, CancellationToken.None);
        var teamInviteMigration = Assert.Single(
            applied,
            migration => migration.StartsWith("13:", StringComparison.Ordinal));
        await fixture.Api.RollbackAsync(teamInviteMigration, CancellationToken.None);
        var latest = Assert.Single(
            applied,
            migration => migration.StartsWith("12:", StringComparison.Ordinal));
        await fixture.Api.RollbackAsync(latest, CancellationToken.None);
        try
        {
            await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                INSERT INTO organizations.organizations (id, version, document)
                VALUES ('{id}', 0, jsonb_build_object(
                    'Id', '{id}',
                    'TenantKey', '{id}',
                    'Name', 'Legacy Organization',
                    'OwnerUserId', 'owner-legacy',
                    'Departments', '[]'::jsonb,
                    'CreatedAt', '2026-07-20T10:00:00+00:00',
                    'UpdatedAt', '2026-07-20T10:00:00+00:00'));
                """);

            await fixture.Api.MigrateAsync(CancellationToken.None);
            await using var provider = new PostgreSqlProvider(fixture.Api.ConnectionString);
            var repository = provider.CreateRepository<OrganizationDocument>("organizations", "organizations");
            var migrated = await repository.SelectAsync(document => document.Id == id);
            Assert.NotNull(migrated);
            Assert.Equal(1, migrated!.Version);
            Assert.Equal(OrganizationStatuses.Active, migrated.Status);
            migrated.Name = "Migrated Organization";
            var changed = await repository.ReplaceByVersionAsync(
                document => document.Id == id,
                migrated,
                migrated.Version);
            Assert.Equal(2, changed.Version);
            await repository.DeleteByFilterAsync(document => document.Id == id);
        }
        finally
        {
            await fixture.Api.MigrateAsync(CancellationToken.None);
        }
    }

    protected override Task<OrganizationRepositoryFixture> CreateFixtureAsync()
    {
        var provider = new PostgreSqlProvider(fixture.Api.ConnectionString);
        return Task.FromResult<OrganizationRepositoryFixture>(new Fixture(provider));
    }

    private sealed class Fixture(PostgreSqlProvider provider)
        : OrganizationRepositoryFixture(
            provider.CreateRepository<OrganizationDocument>("organizations", "organizations"))
    {
        public override async ValueTask DisposeAsync() => await provider.DisposeAsync();
    }
}
