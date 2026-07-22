using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Teams;

namespace Zumbo.RepositoryContracts;

public abstract class TeamRepositoryContract
{
    protected abstract Task<TeamRepositoryFixture> CreateFixtureAsync();

    [Fact]
    public async Task InviteOwnershipAndConcurrency_AreProviderNeutral()
    {
        await using var fixture = await CreateFixtureAsync();
        var stamp = Guid.NewGuid().ToString("N");
        var id = "team-contract-" + stamp;
        var created = await fixture.Repository.CreateAsync(new TeamDocument
        {
            Id = id,
            OrganizationId = "organization-" + stamp,
            Name = "Team Contract",
            Members =
            [
                new TeamMemberDocument
                {
                    Id = "owner-" + stamp,
                    UserId = "user-owner",
                    Email = "owner@zumbo.local",
                    Role = TeamRoles.Owner,
                    Status = TeamMemberStatuses.Active
                },
                new TeamMemberDocument
                {
                    Id = "invite-" + stamp,
                    UserId = "user-invited",
                    Email = "invited@zumbo.local",
                    Role = TeamRoles.Member,
                    Status = TeamMemberStatuses.Invited,
                    InvitationTokenHash = new string('a', 64),
                    InvitedAt = fixture.Now,
                    InvitationExpiresAt = fixture.Now.AddDays(7)
                }
            ],
            CreatedAt = fixture.Now,
            UpdatedAt = fixture.Now
        });
        Assert.Equal(1, created.Version);

        var firstWriter = await fixture.Repository.SelectAsync(team => team.Id == id);
        var staleWriter = await fixture.Repository.SelectAsync(team => team.Id == id);
        Assert.NotNull(firstWriter);
        Assert.NotNull(staleWriter);
        var owner = firstWriter!.Members.Single(member => member.Role == TeamRoles.Owner);
        var invite = firstWriter.Members.Single(member => member.Status == TeamMemberStatuses.Invited);
        owner.Role = TeamRoles.Admin;
        invite.Role = TeamRoles.Owner;
        invite.Status = TeamMemberStatuses.Active;
        invite.InvitationExpiresAt = null;
        invite.RespondedAt = fixture.Now.AddMinutes(1);
        var changed = await fixture.Repository.ReplaceByVersionAsync(
            team => team.Id == id,
            firstWriter,
            firstWriter.Version);
        Assert.Equal(2, changed.Version);

        staleWriter!.Name = "Stale writer";
        var conflict = await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
            fixture.Repository.ReplaceByVersionAsync(
                team => team.Id == id,
                staleWriter,
                staleWriter.Version));
        Assert.Equal(1, conflict.ExpectedVersion);
        Assert.Equal(2, conflict.ActualVersion);

        var persisted = await fixture.Repository.SelectAsync(team => team.Id == id);
        Assert.NotNull(persisted);
        Assert.Single(persisted!.Members, member =>
            member.Role == TeamRoles.Owner && member.Status == TeamMemberStatuses.Active);
        Assert.Equal(new string('a', 64), persisted.Members.Single(member =>
            member.UserId == "user-invited").InvitationTokenHash);
        Assert.Equal(2, persisted.Version);
    }
}

public abstract class TeamRepositoryFixture(
    IDocumentRepository<TeamDocument> repository) : IAsyncDisposable
{
    public IDocumentRepository<TeamDocument> Repository { get; } = repository;
    public DateTimeOffset Now { get; } = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
    public abstract ValueTask DisposeAsync();
}
