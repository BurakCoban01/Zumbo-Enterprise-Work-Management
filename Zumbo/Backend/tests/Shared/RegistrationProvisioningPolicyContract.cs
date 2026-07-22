using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

namespace Zumbo.RepositoryContracts;

public abstract class RegistrationProvisioningPolicyContract
{
    protected abstract Task<RegistrationProvisioningFixture> CreateFixtureAsync();

    [Fact]
    public async Task SystemAdministratorDetection_IsPersistentAndProviderNeutral()
    {
        await using var fixture = await CreateFixtureAsync();
        var repository = new UserRepository(fixture.Users);
        Assert.False(await repository.HasSystemAdminAsync(CancellationToken.None));

        await fixture.Users.CreateAsync(new UserDocument
        {
            Id = "admin-" + Guid.NewGuid().ToString("N"),
            Username = "bootstrap-admin",
            Email = "bootstrap-admin@zumbo.local",
            OrganizationId = "bootstrap-org",
            PasswordHash = "test-only-hash",
            SecurityStamp = "test-security-stamp",
            Roles = ["User", "SystemAdmin"],
            CreatedAt = fixture.Now
        });

        Assert.True(await repository.HasSystemAdminAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProductionLike_RequiresExistingOrganizationAndActiveInvite()
    {
        await using var fixture = await CreateFixtureAsync();
        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "provisioning-" + stamp;
        var invitedEmail = $"invited-{stamp}@zumbo.local";
        var expiredEmail = $"expired-{stamp}@zumbo.local";
        var uninvitedEmail = $"uninvited-{stamp}@zumbo.local";

        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Policy.EnsureAllowedAsync(
            new RegistrationProvisioningRequest(invitedEmail, organizationId, false),
            CancellationToken.None));

        await fixture.Organizations.CreateAsync(new OrganizationDocument
        {
            Id = organizationId,
            TenantKey = organizationId,
            Name = "Provisioning Contract",
            OwnerUserId = "owner-" + stamp,
            CreatedAt = fixture.Now,
            UpdatedAt = fixture.Now
        });

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Policy.EnsureAllowedAsync(
            new RegistrationProvisioningRequest(uninvitedEmail, organizationId, false),
            CancellationToken.None));

        await fixture.Teams.CreateAsync(new TeamDocument
        {
            Id = "team-" + stamp,
            OrganizationId = organizationId,
            Name = "Provisioning Team",
            CreatedAt = fixture.Now,
            UpdatedAt = fixture.Now,
            Members =
            [
                new TeamMemberDocument
                {
                    Email = invitedEmail,
                    Status = "Invited",
                    InvitationExpiresAt = fixture.Now.AddHours(1)
                },
                new TeamMemberDocument
                {
                    Email = expiredEmail,
                    Status = "Invited",
                    InvitationExpiresAt = fixture.Now.AddMinutes(-1)
                }
            ]
        });

        await fixture.Policy.EnsureAllowedAsync(
            new RegistrationProvisioningRequest(invitedEmail, organizationId, false),
            CancellationToken.None);
        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Policy.EnsureAllowedAsync(
            new RegistrationProvisioningRequest(expiredEmail, organizationId, false),
            CancellationToken.None));

        var organization = await fixture.Organizations.SelectAsync(document => document.Id == organizationId);
        Assert.NotNull(organization);
        organization!.Status = OrganizationStatuses.Suspended;
        await fixture.Organizations.ReplaceByVersionAsync(
            document => document.Id == organizationId,
            organization,
            organization.Version);
        var inactive = await Assert.ThrowsAsync<ConflictException>(() => fixture.Policy.EnsureAllowedAsync(
            new RegistrationProvisioningRequest(invitedEmail, organizationId, false),
            CancellationToken.None));
        Assert.Equal("REGISTRATION_ORGANIZATION_INACTIVE", inactive.Code);
    }
}

public abstract class RegistrationProvisioningFixture : IAsyncDisposable
{
    protected RegistrationProvisioningFixture(
        IDocumentRepository<OrganizationDocument> organizations,
        IDocumentRepository<TeamDocument> teams,
        IDocumentRepository<UserDocument> users)
    {
        Organizations = organizations;
        Teams = teams;
        Users = users;
        Now = new DateTimeOffset(2026, 7, 19, 18, 0, 0, TimeSpan.Zero);
        Policy = new RegistrationProvisioningPolicyAdapter(
            organizations,
            teams,
            Options.Create(new RegistrationProvisioningOptions
            {
                Mode = RegistrationProvisioningModes.ProductionLike
            }),
            new ContractHostEnvironment(),
            new ContractClock(Now));
    }

    public IDocumentRepository<OrganizationDocument> Organizations { get; }
    public IDocumentRepository<TeamDocument> Teams { get; }
    public IDocumentRepository<UserDocument> Users { get; }
    public IRegistrationProvisioningPolicy Policy { get; }
    public DateTimeOffset Now { get; }

    public abstract ValueTask DisposeAsync();

    private sealed class ContractClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class ContractHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Zumbo.RepositoryContracts";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
