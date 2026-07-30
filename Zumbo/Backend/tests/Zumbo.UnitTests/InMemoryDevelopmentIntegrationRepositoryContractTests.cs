using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence =
    Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryDevelopmentIntegrationRepositoryContractTests
    : DevelopmentIntegrationRepositoryContract
{
    private readonly InMemoryDocumentRepository<DevelopmentConnectionDocument>
        connections = new();
    private readonly InMemoryDocumentRepository<DevelopmentRepositoryMappingDocument>
        mappings = new();
    private readonly InMemoryDocumentRepository<WorkItemDevelopmentLinkDocument>
        links = new();
    private readonly InMemoryDocumentRepository<DevelopmentWebhookReceiptDocument>
        receipts = new();

    protected override ApplicationPersistence.IDocumentRepository<DevelopmentConnectionDocument>
        Connections() => connections;

    protected override ApplicationPersistence.IDocumentRepository<DevelopmentRepositoryMappingDocument>
        Mappings() => mappings;

    protected override ApplicationPersistence.IDocumentRepository<WorkItemDevelopmentLinkDocument>
        Links() => links;

    protected override ApplicationPersistence.IDocumentRepository<DevelopmentWebhookReceiptDocument>
        Receipts() => receipts;
}
