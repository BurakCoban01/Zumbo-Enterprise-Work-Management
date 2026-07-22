using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlDocumentRepositoryContractTests : DocumentRepositoryContract
{
    private readonly IDocumentRepository<RepositoryContractDocument> _repository;

    public PostgreSqlDocumentRepositoryContractTests(PostgreSqlFixture fixture)
    {
        _repository = fixture.Api.CreateRepository<RepositoryContractDocument>(
            PostgreSqlFixture.TestSchema,
            PostgreSqlFixture.RepositoryTable);
    }

    protected override IDocumentRepository<RepositoryContractDocument> CreateRepository() => _repository;
}
