using Microsoft.Extensions.Configuration;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.RepositoryContracts;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoDocumentRepositoryContractTests : DocumentRepositoryContract
{
    private readonly IDocumentRepository<RepositoryContractDocument> _repository;

    public MongoDocumentRepositoryContractTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required for real Mongo repository contract tests.");
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = "ZumboArch002Contracts"
            })
            .Build();

        _repository = new Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoRepository<RepositoryContractDocument>(
            new Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoDbService(configuration));
    }

    protected override IDocumentRepository<RepositoryContractDocument> CreateRepository() => _repository;
}
