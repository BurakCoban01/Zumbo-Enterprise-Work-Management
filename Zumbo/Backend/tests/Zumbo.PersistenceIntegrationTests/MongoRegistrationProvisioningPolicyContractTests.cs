using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Teams;
using Zumbo.RepositoryContracts;
using ApplicationRepository = Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<Zumbo.Modules.Organizations.OrganizationDocument>;
using ApplicationTeamRepository = Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<Zumbo.Modules.Teams.TeamDocument>;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoRegistrationProvisioningPolicyContractTests
    : RegistrationProvisioningPolicyContract
{
    protected override Task<RegistrationProvisioningFixture> CreateFixtureAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required for provisioning policy contract tests.");
        }

        var databaseName = "ZumboProvisioning" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = databaseName
            })
            .Build();
        var mongo = new MongoDbService(configuration);
        return Task.FromResult<RegistrationProvisioningFixture>(new Fixture(
            new MongoRepository<OrganizationDocument>(mongo),
            new MongoRepository<TeamDocument>(mongo),
            new MongoRepository<Zumbo.Modules.Identity.UserDocument>(mongo),
            mongo,
            databaseName));
    }

    private sealed class Fixture(
        ApplicationRepository organizations,
        ApplicationTeamRepository teams,
        Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<Zumbo.Modules.Identity.UserDocument> users,
        MongoDbService mongo,
        string databaseName) : RegistrationProvisioningFixture(organizations, teams, users)
    {
        public override async ValueTask DisposeAsync() =>
            await mongo.GetClient("Organizations").DropDatabaseAsync(databaseName);
    }
}
