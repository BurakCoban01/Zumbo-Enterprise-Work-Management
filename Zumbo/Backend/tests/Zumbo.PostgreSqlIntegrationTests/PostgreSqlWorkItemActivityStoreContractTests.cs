using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlWorkItemActivityStoreContractTests : WorkItemActivityStoreContract
{
    private readonly PostgreSqlFixture fixture;

    public PostgreSqlWorkItemActivityStoreContractTests(PostgreSqlFixture fixture) => this.fixture = fixture;

    protected override IDocumentRepository<WorkItemCommentActivityDocument> Comments() => fixture.Api.CreateRepository<WorkItemCommentActivityDocument>("work_items", "work_item_comments");
    protected override IDocumentRepository<WorkItemCommentRevisionActivityDocument> Revisions() => fixture.Api.CreateRepository<WorkItemCommentRevisionActivityDocument>("work_items", "work_item_comment_revisions");
    protected override IDocumentRepository<WorkItemAttachmentActivityDocument> Attachments() => fixture.Api.CreateRepository<WorkItemAttachmentActivityDocument>("work_items", "work_item_attachments");
    protected override IDocumentRepository<WorkItemWorkLogActivityDocument> WorkLogs() => fixture.Api.CreateRepository<WorkItemWorkLogActivityDocument>("work_items", "work_item_work_logs");
    protected override IDocumentRepository<WorkItemApprovalActivityDocument> Approvals() => fixture.Api.CreateRepository<WorkItemApprovalActivityDocument>("work_items", "work_item_approvals");
    protected override IDocumentRepository<WorkItemTimelineActivityDocument> Timeline() => fixture.Api.CreateRepository<WorkItemTimelineActivityDocument>("work_items", "work_item_timeline");
}
