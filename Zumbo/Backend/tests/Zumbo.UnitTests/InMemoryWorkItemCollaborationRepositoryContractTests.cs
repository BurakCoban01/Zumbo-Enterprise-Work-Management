using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryWorkItemCollaborationRepositoryContractTests
    : WorkItemCollaborationRepositoryContract
{
    protected override ApplicationPersistence.IDocumentRepository<WorkItemCollaborationDocument> Collaborations() => new InMemoryDocumentRepository<WorkItemCollaborationDocument>();
    protected override ApplicationPersistence.IDocumentRepository<WorkItemEventActivityDocument> Activities() => new InMemoryDocumentRepository<WorkItemEventActivityDocument>();
    protected override ApplicationPersistence.IDocumentRepository<WorkItemTemplateDocument> Templates() => new InMemoryDocumentRepository<WorkItemTemplateDocument>();
    protected override ApplicationPersistence.IDocumentRepository<WorkItemRecurrenceDocument> Recurrences() => new InMemoryDocumentRepository<WorkItemRecurrenceDocument>();
    protected override ApplicationPersistence.IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> Occurrences() => new InMemoryDocumentRepository<WorkItemRecurrenceOccurrenceDocument>();
}
