using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryWorkItemBulkJobRepositoryContractTests : WorkItemBulkJobRepositoryContract
{
    protected override ApplicationPersistence.IDocumentRepository<WorkItemBulkJobDocument> Jobs() =>
        new InMemoryDocumentRepository<WorkItemBulkJobDocument>();
    protected override ApplicationPersistence.IDocumentRepository<WorkItemBulkJobItemDocument> Items() =>
        new InMemoryDocumentRepository<WorkItemBulkJobItemDocument>();
}
