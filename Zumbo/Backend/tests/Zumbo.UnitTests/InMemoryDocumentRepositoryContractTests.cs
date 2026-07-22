using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.RepositoryContracts;

namespace Zumbo.UnitTests;

public sealed class InMemoryDocumentRepositoryContractTests : DocumentRepositoryContract
{
    protected override IDocumentRepository<RepositoryContractDocument> CreateRepository() =>
        new Zumbo.BuildingBlocks.Infrastructure.Persistence.InMemoryDocumentRepository<RepositoryContractDocument>();
}
