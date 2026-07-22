using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.RepositoryContracts;

public abstract class WorkItemActivityStoreContract
{
    protected abstract IDocumentRepository<WorkItemCommentActivityDocument> Comments();
    protected abstract IDocumentRepository<WorkItemCommentRevisionActivityDocument> Revisions();
    protected abstract IDocumentRepository<WorkItemAttachmentActivityDocument> Attachments();
    protected abstract IDocumentRepository<WorkItemWorkLogActivityDocument> WorkLogs();
    protected abstract IDocumentRepository<WorkItemApprovalActivityDocument> Approvals();
    protected abstract IDocumentRepository<WorkItemTimelineActivityDocument> Timeline();

    [Fact]
    public async Task ActivityStore_MigratesComposesPagesAndIsolatesTenantOwnership()
    {
        var repositories = Repositories();
        var store = Store(repositories);
        var prefix = $"data007-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var item = new WorkItemDocument
        {
            Id = prefix + "-item",
            ProjectId = prefix + "-project",
            BoardId = prefix + "-board",
            Comments =
            [
                Comment(prefix + "-comment-1", "first", now),
                Comment(prefix + "-comment-2", "second", now.AddMinutes(1)),
                Comment(prefix + "-comment-3", "third", now.AddMinutes(2))
            ],
            Attachments = [new AttachmentDocument { Id = prefix + "-attachment", FileName = "a.txt", ContentType = "text/plain", SizeBytes = 1, StoragePath = "a", ChecksumSha256 = "a", CreatedAt = now }],
            WorkLogs = [new WorkLogDocument { Id = prefix + "-log", UserId = "user-a", Hours = 2.5m, CreatedAt = now }],
            Approvals = [new WorkItemApprovalDocument { Id = prefix + "-approval", FromStatus = "To Do", ToStatus = "Done", RequestedAt = now, ExpiresAt = now.AddDays(1) }],
            StatusHistory = [new WorkItemStatusHistoryDocument { ToStatus = "To Do", ChangedAt = now }]
        };

        try
        {
            Assert.True(await store.MigrateEmbeddedAsync(item, "org-a", CancellationToken.None));
            Assert.False(await store.MigrateEmbeddedAsync(item, "org-a", CancellationToken.None));
            item.Comments = [];
            item.Attachments = [];
            item.WorkLogs = [];
            item.Approvals = [];
            item.StatusHistory = [];
            await store.HydrateAsync(item, "org-a", CancellationToken.None);

            Assert.Equal(3, item.Comments.Count);
            Assert.Single(item.Comments[0].History);
            Assert.Single(item.Attachments);
            Assert.Single(item.WorkLogs);
            Assert.Single(item.Approvals);
            Assert.Single(item.StatusHistory);

            var page = await store.ListCommentsAsync(
                "org-a", item.ProjectId, item.Id, 2, 1, CancellationToken.None);
            Assert.Equal(3, page.TotalCount);
            Assert.Equal("second", Assert.Single(page.Items).Body);
            Assert.Single((await store.ListRevisionsAsync(
                "org-a", item.ProjectId, item.Id, prefix + "-comment-1", 1, 1, CancellationToken.None)).Items);
            Assert.Single((await store.ListAttachmentsAsync(
                "org-a", item.ProjectId, item.Id, 1, 1, CancellationToken.None)).Items);
            Assert.Single((await store.ListWorkLogsAsync(
                "org-a", item.ProjectId, item.Id, 1, 1, CancellationToken.None)).Items);
            Assert.Single((await store.ListApprovalsAsync(
                "org-a", item.ProjectId, item.Id, 1, 1, CancellationToken.None)).Items);
            Assert.Single((await store.ListTimelineAsync(
                "org-a", item.ProjectId, item.Id, 1, 1, CancellationToken.None)).Items);
            Assert.Empty((await store.ListCommentsAsync(
                "org-b", item.ProjectId, item.Id, 1, 100, CancellationToken.None)).Items);
            Assert.Empty((await store.ListAttachmentsAsync(
                "org-b", item.ProjectId, item.Id, 1, 100, CancellationToken.None)).Items);
            Assert.Empty((await store.ListWorkLogsAsync(
                "org-b", item.ProjectId, item.Id, 1, 100, CancellationToken.None)).Items);
            Assert.Empty((await store.ListApprovalsAsync(
                "org-b", item.ProjectId, item.Id, 1, 100, CancellationToken.None)).Items);
            Assert.Empty((await store.ListTimelineAsync(
                "org-b", item.ProjectId, item.Id, 1, 100, CancellationToken.None)).Items);
            Assert.Null(await store.GetCommentAsync(
                "org-b", item.ProjectId, item.Id, prefix + "-comment-1", CancellationToken.None));

            var revisionReferences = await store.FindUserReferencesAsync(
                "org-a", "user-a", CancellationToken.None);
            Assert.True(revisionReferences[item.Id].CommentRevision);
        }
        finally
        {
            await CleanupAsync(repositories, prefix);
        }
    }

    [Fact]
    public async Task ActivityStore_RejectsStaleConcurrentCommentUpdate()
    {
        var repositories = Repositories();
        var store = Store(repositories);
        var prefix = $"data007-cas-{Guid.NewGuid():N}";
        var document = new WorkItemCommentActivityDocument
        {
            Id = prefix + "-comment",
            OrganizationId = "org-a",
            ProjectId = prefix + "-project",
            WorkItemId = prefix + "-item",
            Body = "initial",
            AuthorUserId = "user-a",
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await store.CreateCommentAsync(document, CancellationToken.None);
            var first = await store.GetCommentAsync("org-a", document.ProjectId, document.WorkItemId, document.Id, CancellationToken.None);
            var stale = await store.GetCommentAsync("org-a", document.ProjectId, document.WorkItemId, document.Id, CancellationToken.None);
            first!.Body = "first";
            await store.UpdateCommentAsync(first, CancellationToken.None);
            stale!.Body = "stale";
            await Assert.ThrowsAsync<DocumentConcurrencyException>(
                () => store.UpdateCommentAsync(stale, CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(repositories, prefix);
        }
    }

    [Fact]
    public async Task ActivityStore_RejectsIncompatibleLegacyReconciliation()
    {
        var repositories = Repositories();
        var store = Store(repositories);
        var prefix = $"data007-reconcile-{Guid.NewGuid():N}";
        var item = new WorkItemDocument
        {
            Id = prefix + "-item",
            ProjectId = prefix + "-project",
            BoardId = prefix + "-board",
            Comments = [Comment(prefix + "-comment", "legacy-source", DateTimeOffset.UtcNow)]
        };
        var conflicting = new WorkItemCommentActivityDocument
        {
            Id = item.Comments[0].Id,
            OrganizationId = "org-a",
            ProjectId = item.ProjectId,
            WorkItemId = item.Id,
            Body = "different-target",
            AuthorUserId = item.Comments[0].AuthorUserId,
            Mentions = [.. item.Comments[0].Mentions],
            CreatedAt = item.Comments[0].CreatedAt,
            EditedAt = item.Comments[0].EditedAt
        };

        try
        {
            await store.CreateCommentAsync(conflicting, CancellationToken.None);
            var exception = await Assert.ThrowsAsync<ConflictException>(
                () => store.MigrateEmbeddedAsync(item, "org-a", CancellationToken.None));
            Assert.Equal("WORK_ITEM_ACTIVITY_MIGRATION_CONFLICT", exception.Code);
        }
        finally
        {
            await CleanupAsync(repositories, prefix);
        }
    }

    private RepositoriesSet Repositories() => new(
        Comments(), Revisions(), Attachments(), WorkLogs(), Approvals(), Timeline());

    private static WorkItemActivityStore Store(RepositoriesSet x) => new(
        x.Comments, x.Revisions, x.Attachments, x.WorkLogs, x.Approvals, x.Timeline);

    private static CommentDocument Comment(string id, string body, DateTimeOffset createdAt) => new()
    {
        Id = id,
        Body = body,
        AuthorUserId = "user-a",
        CreatedAt = createdAt,
        History = [new CommentRevisionDocument { Body = body + "-old", EditedByUserId = "user-a", EditedAt = createdAt.AddSeconds(1) }]
    };

    private static async Task CleanupAsync(RepositoriesSet x, string prefix)
    {
        await x.Comments.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
        await x.Revisions.DeleteByFilterAsync(document => document.WorkItemId.StartsWith(prefix));
        await x.Attachments.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
        await x.WorkLogs.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
        await x.Approvals.DeleteByFilterAsync(document => document.Id.StartsWith(prefix));
        await x.Timeline.DeleteByFilterAsync(document => document.WorkItemId.StartsWith(prefix));
    }

    private sealed record RepositoriesSet(
        IDocumentRepository<WorkItemCommentActivityDocument> Comments,
        IDocumentRepository<WorkItemCommentRevisionActivityDocument> Revisions,
        IDocumentRepository<WorkItemAttachmentActivityDocument> Attachments,
        IDocumentRepository<WorkItemWorkLogActivityDocument> WorkLogs,
        IDocumentRepository<WorkItemApprovalActivityDocument> Approvals,
        IDocumentRepository<WorkItemTimelineActivityDocument> Timeline);
}
