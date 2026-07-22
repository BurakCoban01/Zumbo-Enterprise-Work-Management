using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryWorkItemActivityStoreContractTests : WorkItemActivityStoreContract
{
    protected override ApplicationPersistence.IDocumentRepository<WorkItemCommentActivityDocument> Comments() => new InMemoryDocumentRepository<WorkItemCommentActivityDocument>();
    protected override ApplicationPersistence.IDocumentRepository<WorkItemCommentRevisionActivityDocument> Revisions() => new InMemoryDocumentRepository<WorkItemCommentRevisionActivityDocument>();
    protected override ApplicationPersistence.IDocumentRepository<WorkItemAttachmentActivityDocument> Attachments() => new InMemoryDocumentRepository<WorkItemAttachmentActivityDocument>();
    protected override ApplicationPersistence.IDocumentRepository<WorkItemWorkLogActivityDocument> WorkLogs() => new InMemoryDocumentRepository<WorkItemWorkLogActivityDocument>();
    protected override ApplicationPersistence.IDocumentRepository<WorkItemApprovalActivityDocument> Approvals() => new InMemoryDocumentRepository<WorkItemApprovalActivityDocument>();
    protected override ApplicationPersistence.IDocumentRepository<WorkItemTimelineActivityDocument> Timeline() => new InMemoryDocumentRepository<WorkItemTimelineActivityDocument>();
}
