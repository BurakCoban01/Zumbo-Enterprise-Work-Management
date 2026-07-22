using Zumbo.Modules.Projects;
using Zumbo.Persistence.PostgreSql;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlProjectRepositoryContractTests(PostgreSqlFixture fixture)
    : ProjectRepositoryContract
{
    [Fact]
    public async Task Migration14_BackfillsLegacyProjectLifecycleForCas()
    {
        var id = "legacy-project-" + Guid.NewGuid().ToString("N");
        var latest = Assert.Single(
            await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None),
            migration => migration.StartsWith("14:", StringComparison.Ordinal));
        await fixture.Api.RollbackAsync(latest, CancellationToken.None);
        try
        {
            await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                INSERT INTO projects.projects (id, version, document)
                VALUES ('{id}', 0, jsonb_build_object(
                    'Id', '{id}',
                    'OrganizationId', 'org-legacy',
                    'Key', 'LEGACY',
                    'Name', 'Legacy Project',
                    'Members', jsonb_build_array(jsonb_build_object(
                        'UserId', 'owner-legacy',
                        'Role', 'ProjectOwner')),
                    'TeamIds', '[]'::jsonb,
                    'CreatedAt', '2026-07-20T14:00:00+00:00',
                    'UpdatedAt', '2026-07-20T14:00:00+00:00'));
                """);

            await fixture.Api.MigrateAsync(CancellationToken.None);
            await using var provider = new PostgreSqlProvider(fixture.Api.ConnectionString);
            var repository = provider.CreateRepository<ProjectDocument>("projects", "projects");
            var migrated = await repository.SelectAsync(project => project.Id == id);
            Assert.NotNull(migrated);
            Assert.Equal(1, migrated!.Version);
            Assert.Equal(ProjectVisibilities.Internal, migrated.Visibility);
            Assert.Empty(migrated.Templates);
            Assert.Empty(migrated.Components);
            Assert.Empty(migrated.Releases);
            migrated.Name = "Migrated Project";
            var changed = await repository.ReplaceByVersionAsync(
                project => project.Id == id,
                migrated,
                migrated.Version);
            Assert.Equal(2, changed.Version);
            await repository.DeleteByFilterAsync(project => project.Id == id);
        }
        finally
        {
            await fixture.Api.MigrateAsync(CancellationToken.None);
        }
    }

    protected override Task<ProjectRepositoryFixture> CreateFixtureAsync()
    {
        var provider = new PostgreSqlProvider(fixture.Api.ConnectionString);
        return Task.FromResult<ProjectRepositoryFixture>(new Fixture(provider));
    }

    private sealed class Fixture(PostgreSqlProvider provider)
        : ProjectRepositoryFixture(provider.CreateRepository<ProjectDocument>("projects", "projects"))
    {
        public override async ValueTask DisposeAsync() => await provider.DisposeAsync();
    }
}
