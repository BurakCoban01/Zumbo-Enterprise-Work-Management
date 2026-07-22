using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Teams;
using Zumbo.Persistence.PostgreSql;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlRegistrationProvisioningPolicyContractTests(PostgreSqlFixture fixture)
    : RegistrationProvisioningPolicyContract
{
    protected override Task<RegistrationProvisioningFixture> CreateFixtureAsync()
    {
        var provider = new PostgreSqlProvider(fixture.Api.ConnectionString);
        return Task.FromResult<RegistrationProvisioningFixture>(new Fixture(
            provider.CreateRepository<OrganizationDocument>("organizations", "organizations"),
            provider.CreateRepository<TeamDocument>("teams", "teams"),
            provider.CreateRepository<Zumbo.Modules.Identity.UserDocument>("identity", "users"),
            provider));
    }

    private sealed class Fixture(
        IDocumentRepository<OrganizationDocument> organizations,
        IDocumentRepository<TeamDocument> teams,
        IDocumentRepository<Zumbo.Modules.Identity.UserDocument> users,
        PostgreSqlProvider provider) : RegistrationProvisioningFixture(organizations, teams, users)
    {
        public override async ValueTask DisposeAsync() => await provider.DisposeAsync();
    }
}
