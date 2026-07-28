using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryKnowledgeRepositoryContractTests : KnowledgeRepositoryContract
{
    private readonly InMemoryDocumentRepository<KnowledgeDocument> documents = new();

    protected override ApplicationPersistence.IDocumentRepository<KnowledgeDocument> Documents() =>
        documents;
}
