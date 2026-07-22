using Zumbo.Modules.Teams;
using Zumbo.Persistence.PostgreSql;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlTeamRepositoryContractTests(PostgreSqlFixture fixture)
    : TeamRepositoryContract
{
    [Fact]
    public async Task Migration13_ExpiresLegacyHashlessInvites()
    {
        var id = "legacy-team-" + Guid.NewGuid().ToString("N");
        var applied = await fixture.Api.GetAppliedMigrationsAsync(CancellationToken.None);
        var projectMigration = Assert.Single(
            applied,
            migration => migration.StartsWith("14:", StringComparison.Ordinal));
        await fixture.Api.RollbackAsync(projectMigration, CancellationToken.None);
        var latest = Assert.Single(
            applied,
            migration => migration.StartsWith("13:", StringComparison.Ordinal));
        await fixture.Api.RollbackAsync(latest, CancellationToken.None);
        try
        {
            await using var connection = await fixture.Api.OpenConnectionAsync(CancellationToken.None);
            await PostgreSqlFixture.ExecuteAsync(connection, $"""
                INSERT INTO teams.teams (id, version, document)
                VALUES ('{id}', 1, jsonb_build_object(
                    'Id', '{id}',
                    'OrganizationId', 'org-legacy',
                    'Name', 'Legacy Team',
                    'Archived', false,
                    'Version', 1,
                    'Members', jsonb_build_array(
                        jsonb_build_object(
                            'Id', 'owner',
                            'UserId', 'owner-user',
                            'Email', 'owner@zumbo.local',
                            'Role', 'Owner',
                            'Status', 'Active'),
                        jsonb_build_object(
                            'Id', 'legacy-invite',
                            'UserId', 'invite-user',
                            'Email', 'invite@zumbo.local',
                            'Role', 'Member',
                            'Status', 'Invited',
                            'InvitationExpiresAt', '2026-07-27T12:00:00+00:00')),
                    'CreatedAt', '2026-07-20T12:00:00+00:00',
                    'UpdatedAt', '2026-07-20T12:00:00+00:00'));
                """);

            await fixture.Api.MigrateAsync(CancellationToken.None);
            await using var provider = new PostgreSqlProvider(fixture.Api.ConnectionString);
            var repository = provider.CreateRepository<TeamDocument>("teams", "teams");
            var migrated = await repository.SelectAsync(team => team.Id == id);
            Assert.NotNull(migrated);
            Assert.Equal(2, migrated!.Version);
            var invite = migrated.Members.Single(member => member.Id == "legacy-invite");
            Assert.Equal(TeamMemberStatuses.Expired, invite.Status);
            Assert.Null(invite.InvitationTokenHash);
            Assert.NotNull(invite.RespondedAt);
            await repository.DeleteByFilterAsync(team => team.Id == id);
        }
        finally
        {
            await fixture.Api.MigrateAsync(CancellationToken.None);
        }
    }

    protected override Task<TeamRepositoryFixture> CreateFixtureAsync()
    {
        var provider = new PostgreSqlProvider(fixture.Api.ConnectionString);
        return Task.FromResult<TeamRepositoryFixture>(new Fixture(provider));
    }

    private sealed class Fixture(PostgreSqlProvider provider)
        : TeamRepositoryFixture(provider.CreateRepository<TeamDocument>("teams", "teams"))
    {
        public override async ValueTask DisposeAsync() => await provider.DisposeAsync();
    }
}
