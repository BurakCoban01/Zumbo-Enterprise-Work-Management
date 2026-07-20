using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public interface IWorkItemActivityDocument : IVersionedDocument
{
    string OrganizationId { get; set; }
    string ProjectId { get; set; }
    string WorkItemId { get; set; }
}

public sealed class WorkItemCommentActivityDocument : IWorkItemActivityDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = "system";
    public List<string> Mentions { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemCommentRevisionActivityDocument : IWorkItemActivityDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string CommentId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string EditedByUserId { get; set; } = "system";
    public DateTimeOffset EditedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemAttachmentActivityDocument : IWorkItemActivityDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string SecurityState { get; set; } = AttachmentSecurityStates.Clean;
    public string ScanProvider { get; set; } = "Legacy";
    public string? ScanDetail { get; set; }
    public DateTimeOffset? ScannedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemWorkLogActivityDocument : IWorkItemActivityDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public decimal Hours { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemApprovalActivityDocument : IWorkItemActivityDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = "system";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemTimelineActivityDocument : IWorkItemActivityDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string ChangedByUserId { get; set; } = "system";
    public DateTimeOffset ChangedAt { get; set; }
    public long Version { get; set; }
}

public sealed record WorkItemActivityPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount);

public sealed record WorkItemUserActivityReference(
    string WorkItemId,
    bool CommentAuthor,
    bool CommentRevision,
    bool Mention,
    bool WorkLog,
    bool Approval,
    bool Timeline);

public sealed record WorkItemReportActivityData(
    IReadOnlyDictionary<string, decimal> LoggedHoursByWorkItem,
    IReadOnlyDictionary<string, IReadOnlyList<WorkItemStatusHistoryResponse>> TimelineByWorkItem);

public interface IWorkItemActivityStore
{
    Task<bool> MigrateEmbeddedAsync(WorkItemDocument workItem, string organizationId, CancellationToken ct);
    Task HydrateAsync(WorkItemDocument workItem, string organizationId, CancellationToken ct);

    Task CreateCommentAsync(WorkItemCommentActivityDocument comment, CancellationToken ct);
    Task<WorkItemCommentActivityDocument?> GetCommentAsync(
        string organizationId, string projectId, string workItemId, string commentId, CancellationToken ct);
    Task UpdateCommentAsync(WorkItemCommentActivityDocument comment, CancellationToken ct);
    Task DeleteCommentAsync(WorkItemCommentActivityDocument comment, CancellationToken ct);
    Task CreateRevisionAsync(WorkItemCommentRevisionActivityDocument revision, CancellationToken ct);
    Task CreateAttachmentAsync(WorkItemAttachmentActivityDocument attachment, CancellationToken ct);
    Task<WorkItemAttachmentActivityDocument?> GetAttachmentAsync(
        string organizationId, string projectId, string workItemId, string attachmentId, CancellationToken ct);
    Task DeleteAttachmentAsync(WorkItemAttachmentActivityDocument attachment, CancellationToken ct);
    Task CreateWorkLogAsync(WorkItemWorkLogActivityDocument workLog, CancellationToken ct);
    Task CreateApprovalAsync(WorkItemApprovalActivityDocument approval, CancellationToken ct);
    Task<WorkItemApprovalActivityDocument?> GetApprovalAsync(
        string organizationId, string projectId, string workItemId, string approvalId, CancellationToken ct);
    Task UpdateApprovalAsync(WorkItemApprovalActivityDocument approval, CancellationToken ct);
    Task CreateTimelineAsync(WorkItemTimelineActivityDocument timeline, CancellationToken ct);

    Task<WorkItemActivityPage<CommentResponse>> ListCommentsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct);
    Task<WorkItemActivityPage<CommentRevisionResponse>> ListRevisionsAsync(
        string organizationId, string projectId, string workItemId, string commentId, int page, int pageSize, CancellationToken ct);
    Task<WorkItemActivityPage<AttachmentResponse>> ListAttachmentsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct);
    Task<WorkItemActivityPage<WorkLogResponse>> ListWorkLogsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct);
    Task<WorkItemActivityPage<WorkItemApprovalResponse>> ListApprovalsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct);
    Task<WorkItemActivityPage<WorkItemStatusHistoryResponse>> ListTimelineAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyDictionary<string, WorkItemUserActivityReference>> FindUserReferencesAsync(
        string organizationId, string userId, CancellationToken ct);
    IAsyncEnumerable<WorkItemUserActivityReference> StreamUserReferencesAsync(
        string organizationId, string userId, CancellationToken ct);
    Task<WorkItemReportActivityData> ReadReportDataAsync(
        string organizationId, string projectId, CancellationToken ct);
    Task AnonymizeUserReferencesAsync(
        string organizationId, string userId, string pseudonym, CancellationToken ct);
}

public sealed class WorkItemActivityQueryService(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemActivityStore activities,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser)
{
    public async Task<WorkItemActivityPage<CommentResponse>> ListCommentsAsync(
        string workItemId, int page, int pageSize, CancellationToken ct)
    {
        var authorized = await GetAsync(workItemId, ct);
        var item = authorized.Item;
        return item.ActivityStorageVersion >= 1
            ? await activities.ListCommentsAsync(authorized.OrganizationId, item.ProjectId, item.Id, page, pageSize, ct)
            : Page(item.Comments.Select(ToResponse), page, pageSize);
    }

    public async Task<WorkItemActivityPage<CommentRevisionResponse>> ListRevisionsAsync(
        string workItemId, string commentId, int page, int pageSize, CancellationToken ct)
    {
        var authorized = await GetAsync(workItemId, ct);
        var item = authorized.Item;
        if (item.ActivityStorageVersion >= 1)
        {
            return await activities.ListRevisionsAsync(
                authorized.OrganizationId, item.ProjectId, item.Id, commentId, page, pageSize, ct);
        }

        var comment = item.Comments.SingleOrDefault(x => x.Id == commentId)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");
        return Page(
            comment.History.OrderBy(x => x.EditedAt)
                .Select(x => new CommentRevisionResponse(x.Body, x.EditedByUserId, x.EditedAt)),
            page,
            pageSize);
    }

    public Task<WorkItemActivityPage<AttachmentResponse>> ListAttachmentsAsync(
        string workItemId, int page, int pageSize, CancellationToken ct) =>
        ReadAsync(workItemId, page, pageSize,
            (organizationId, item) => activities.ListAttachmentsAsync(
                organizationId, item.ProjectId, item.Id, page, pageSize, ct),
            item => item.Attachments.Select(x => new AttachmentResponse(
                x.Id, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt,
                x.SecurityState, x.ScanProvider, x.ScannedAt)), ct);

    public Task<WorkItemActivityPage<WorkLogResponse>> ListWorkLogsAsync(
        string workItemId, int page, int pageSize, CancellationToken ct) =>
        ReadAsync(workItemId, page, pageSize,
            (organizationId, item) => activities.ListWorkLogsAsync(
                organizationId, item.ProjectId, item.Id, page, pageSize, ct),
            item => item.WorkLogs.Select(x => new WorkLogResponse(
                x.Id, x.UserId, x.Hours, x.Note, x.CreatedAt)), ct);

    public Task<WorkItemActivityPage<WorkItemApprovalResponse>> ListApprovalsAsync(
        string workItemId, int page, int pageSize, CancellationToken ct) =>
        ReadAsync(workItemId, page, pageSize,
            (organizationId, item) => activities.ListApprovalsAsync(
                organizationId, item.ProjectId, item.Id, page, pageSize, ct),
            item => item.Approvals.Select(ToResponse), ct);

    public Task<WorkItemActivityPage<WorkItemStatusHistoryResponse>> ListTimelineAsync(
        string workItemId, int page, int pageSize, CancellationToken ct) =>
        ReadAsync(workItemId, page, pageSize,
            (organizationId, item) => activities.ListTimelineAsync(
                organizationId, item.ProjectId, item.Id, page, pageSize, ct),
            item => item.StatusHistory.Select(x => new WorkItemStatusHistoryResponse(
                x.FromStatus, x.ToStatus, x.ChangedByUserId, x.ChangedAt)), ct);

    private async Task<WorkItemActivityPage<T>> ReadAsync<T>(
        string workItemId,
        int page,
        int pageSize,
        Func<string, WorkItemDocument, Task<WorkItemActivityPage<T>>> separated,
        Func<WorkItemDocument, IEnumerable<T>> legacy,
        CancellationToken ct)
    {
        var authorized = await GetAsync(workItemId, ct);
        var item = authorized.Item;
        return item.ActivityStorageVersion >= 1
            ? await separated(authorized.OrganizationId, item)
            : Page(legacy(item), page, pageSize);
    }

    private async Task<AuthorizedWorkItem> GetAsync(string workItemId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var item = await workItems.SelectAsync(x => x.Id == workItemId && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await permissionChecker.EnsureCanAsync(
            userId,
            item.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        return new AuthorizedWorkItem(item, authorization.OrganizationId);
    }

    private sealed record AuthorizedWorkItem(WorkItemDocument Item, string OrganizationId);

    private static WorkItemActivityPage<T> Page<T>(IEnumerable<T> source, int page, int pageSize)
    {
        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var items = source.ToList();
        return new(items.Skip((safePage - 1) * safeSize).Take(safeSize).ToList(), safePage, safeSize, items.Count);
    }

    private static CommentResponse ToResponse(CommentDocument x) => new(
        x.Id,
        x.Body,
        x.AuthorUserId,
        x.Mentions,
        x.CreatedAt,
        x.EditedAt,
        x.History.OrderBy(revision => revision.EditedAt)
            .Select(revision => new CommentRevisionResponse(
                revision.Body, revision.EditedByUserId, revision.EditedAt)).ToList());

    private static WorkItemApprovalResponse ToResponse(WorkItemApprovalDocument x) => new(
        x.Id,
        x.FromStatus,
        x.ToStatus,
        x.RequestedByUserId,
        x.RequestedAt,
        x.ExpiresAt,
        x.Status,
        x.DecidedByUserId,
        x.DecidedAt,
        x.Note,
        x.ConsumedAt);
}

public sealed class WorkItemActivityStore(
    IDocumentRepository<WorkItemCommentActivityDocument> comments,
    IDocumentRepository<WorkItemCommentRevisionActivityDocument> revisions,
    IDocumentRepository<WorkItemAttachmentActivityDocument> attachments,
    IDocumentRepository<WorkItemWorkLogActivityDocument> workLogs,
    IDocumentRepository<WorkItemApprovalActivityDocument> approvals,
    IDocumentRepository<WorkItemTimelineActivityDocument> timeline) : IWorkItemActivityStore
{
    public async Task<bool> MigrateEmbeddedAsync(
        WorkItemDocument workItem,
        string organizationId,
        CancellationToken ct)
    {
        ValidateOwnership(organizationId, workItem.ProjectId, workItem.Id);
        if (workItem.ActivityStorageVersion >= 1)
        {
            return false;
        }

        foreach (var comment in workItem.Comments)
        {
            var expectedComment = ToActivity(workItem, organizationId, comment);
            await CreateOrValidateAsync(
                comments,
                expectedComment,
                stored => SameOwner(stored, organizationId, workItem.ProjectId, workItem.Id)
                    && SamePayload(stored, expectedComment),
                ct);
            for (var index = 0; index < comment.History.Count; index++)
            {
                var expectedRevision = ToActivity(
                    workItem, organizationId, comment.Id, comment.History[index], index);
                await CreateOrValidateAsync(
                    revisions,
                    expectedRevision,
                    stored => SameOwner(stored, organizationId, workItem.ProjectId, workItem.Id)
                        && stored.CommentId == comment.Id
                        && SamePayload(stored, expectedRevision),
                    ct);
            }
        }

        foreach (var attachment in workItem.Attachments)
        {
            var expected = ToActivity(workItem, organizationId, attachment);
            await CreateOrValidateAsync(
                attachments,
                expected,
                stored => SameOwner(stored, organizationId, workItem.ProjectId, workItem.Id)
                    && SamePayload(stored, expected),
                ct);
        }

        foreach (var workLog in workItem.WorkLogs)
        {
            var expected = ToActivity(workItem, organizationId, workLog);
            await CreateOrValidateAsync(
                workLogs,
                expected,
                stored => SameOwner(stored, organizationId, workItem.ProjectId, workItem.Id)
                    && SamePayload(stored, expected),
                ct);
        }

        foreach (var approval in workItem.Approvals)
        {
            var expected = ToActivity(workItem, organizationId, approval);
            await CreateOrValidateAsync(
                approvals,
                expected,
                stored => SameOwner(stored, organizationId, workItem.ProjectId, workItem.Id)
                    && SamePayload(stored, expected),
                ct);
        }

        for (var index = 0; index < workItem.StatusHistory.Count; index++)
        {
            var expected = ToActivity(workItem, organizationId, workItem.StatusHistory[index], index);
            await CreateOrValidateAsync(
                timeline,
                expected,
                stored => SameOwner(stored, organizationId, workItem.ProjectId, workItem.Id)
                    && SamePayload(stored, expected),
                ct);
        }

        workItem.ActivityStorageVersion = 1;
        return true;
    }

    public async Task HydrateAsync(WorkItemDocument workItem, string organizationId, CancellationToken ct)
    {
        ValidateOwnership(organizationId, workItem.ProjectId, workItem.Id);
        if (workItem.ActivityStorageVersion < 1)
        {
            return;
        }

        var storedComments = await LoadAllAsync(
            comments,
            x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
            ct);
        var storedRevisions = await LoadAllAsync(
            revisions,
            x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
            ct);
        workItem.Comments = storedComments
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(comment => new CommentDocument
            {
                Id = comment.Id,
                Body = comment.Body,
                AuthorUserId = comment.AuthorUserId,
                Mentions = [.. comment.Mentions],
                CreatedAt = comment.CreatedAt,
                EditedAt = comment.EditedAt,
                History = storedRevisions
                    .Where(x => x.CommentId == comment.Id)
                    .OrderBy(x => x.EditedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
                    .Select(x => new CommentRevisionDocument
                    {
                        Body = x.Body,
                        EditedByUserId = x.EditedByUserId,
                        EditedAt = x.EditedAt
                    }).ToList()
            }).ToList();

        workItem.Attachments = (await LoadAllAsync(
                attachments,
                x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
                ct))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(ToEmbedded).ToList();
        workItem.WorkLogs = (await LoadAllAsync(
                workLogs,
                x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
                ct))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(ToEmbedded).ToList();
        workItem.Approvals = (await LoadAllAsync(
                approvals,
                x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
                ct))
            .OrderBy(x => x.RequestedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(ToEmbedded).ToList();
        workItem.StatusHistory = (await LoadAllAsync(
                timeline,
                x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
                ct))
            .OrderBy(x => x.ChangedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(ToEmbedded).ToList();
    }

    public Task CreateCommentAsync(WorkItemCommentActivityDocument comment, CancellationToken ct) =>
        CreateOwnedAsync(comments, comment, ct);

    public Task<WorkItemCommentActivityDocument?> GetCommentAsync(
        string organizationId, string projectId, string workItemId, string commentId, CancellationToken ct) =>
        comments.SelectAsync(x => x.Id == commentId
            && x.OrganizationId == organizationId
            && x.ProjectId == projectId
            && x.WorkItemId == workItemId, ct);

    public Task UpdateCommentAsync(WorkItemCommentActivityDocument comment, CancellationToken ct) =>
        ReplaceOwnedAsync(comments, comment, ct);

    public async Task DeleteCommentAsync(WorkItemCommentActivityDocument comment, CancellationToken ct)
    {
        ValidateOwnership(comment.OrganizationId, comment.ProjectId, comment.WorkItemId);
        await revisions.DeleteByFilterAsync(x => x.OrganizationId == comment.OrganizationId
            && x.ProjectId == comment.ProjectId
            && x.WorkItemId == comment.WorkItemId
            && x.CommentId == comment.Id, ct);
        var deleted = await comments.DeleteByFilterAsync(x => x.Id == comment.Id
            && x.OrganizationId == comment.OrganizationId
            && x.ProjectId == comment.ProjectId
            && x.WorkItemId == comment.WorkItemId
            && x.Version == comment.Version, ct);
        if (deleted == 0)
        {
            throw new ConflictException("COMMENT_CONCURRENTLY_CHANGED", "Comment changed before it could be deleted.");
        }
    }

    public Task CreateRevisionAsync(WorkItemCommentRevisionActivityDocument revision, CancellationToken ct) =>
        CreateOwnedAsync(revisions, revision, ct);

    public Task CreateAttachmentAsync(WorkItemAttachmentActivityDocument attachment, CancellationToken ct) =>
        CreateOwnedAsync(attachments, attachment, ct);

    public Task<WorkItemAttachmentActivityDocument?> GetAttachmentAsync(
        string organizationId, string projectId, string workItemId, string attachmentId, CancellationToken ct) =>
        attachments.SelectAsync(x => x.Id == attachmentId
            && x.OrganizationId == organizationId
            && x.ProjectId == projectId
            && x.WorkItemId == workItemId, ct);

    public async Task DeleteAttachmentAsync(WorkItemAttachmentActivityDocument attachment, CancellationToken ct)
    {
        ValidateOwnership(attachment.OrganizationId, attachment.ProjectId, attachment.WorkItemId);
        var deleted = await attachments.DeleteByFilterAsync(x => x.Id == attachment.Id
            && x.OrganizationId == attachment.OrganizationId
            && x.ProjectId == attachment.ProjectId
            && x.WorkItemId == attachment.WorkItemId
            && x.Version == attachment.Version, ct);
        if (deleted == 0)
        {
            throw new ConflictException("ATTACHMENT_CONCURRENTLY_CHANGED", "Attachment changed before it could be deleted.");
        }
    }

    public Task CreateWorkLogAsync(WorkItemWorkLogActivityDocument workLog, CancellationToken ct) =>
        CreateOwnedAsync(workLogs, workLog, ct);

    public Task CreateApprovalAsync(WorkItemApprovalActivityDocument approval, CancellationToken ct) =>
        CreateOwnedAsync(approvals, approval, ct);

    public Task<WorkItemApprovalActivityDocument?> GetApprovalAsync(
        string organizationId, string projectId, string workItemId, string approvalId, CancellationToken ct) =>
        approvals.SelectAsync(x => x.Id == approvalId
            && x.OrganizationId == organizationId
            && x.ProjectId == projectId
            && x.WorkItemId == workItemId, ct);

    public Task UpdateApprovalAsync(WorkItemApprovalActivityDocument approval, CancellationToken ct) =>
        ReplaceOwnedAsync(approvals, approval, ct);

    public Task CreateTimelineAsync(WorkItemTimelineActivityDocument entry, CancellationToken ct) =>
        CreateOwnedAsync(timeline, entry, ct);

    public async Task<WorkItemReportActivityData> ReadReportDataAsync(
        string organizationId,
        string projectId,
        CancellationToken ct)
    {
        ValidateOwnership(organizationId, projectId, "report");
        var projectWorkLogs = await LoadAllAsync(
            workLogs,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId,
            ct);
        var projectTimeline = await LoadAllAsync(
            timeline,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId,
            ct);

        return new WorkItemReportActivityData(
            projectWorkLogs
                .GroupBy(x => x.WorkItemId, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Sum(log => log.Hours), StringComparer.Ordinal),
            projectTimeline
                .GroupBy(x => x.WorkItemId, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => (IReadOnlyList<WorkItemStatusHistoryResponse>)x
                        .OrderBy(entry => entry.ChangedAt)
                        .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                        .Select(entry => new WorkItemStatusHistoryResponse(
                            entry.FromStatus,
                            entry.ToStatus,
                            entry.ChangedByUserId,
                            entry.ChangedAt))
                        .ToList(),
                    StringComparer.Ordinal));
    }

    public async Task<WorkItemActivityPage<CommentResponse>> ListCommentsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct)
    {
        var normalized = NormalizePage(page, pageSize);
        Expression<Func<WorkItemCommentActivityDocument, bool>> filter = x =>
            x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
        var items = await comments.ListByFilterAsync(filter, x => x.CreatedAt,
            page: normalized.Page, pageSize: normalized.PageSize, cancellationToken: ct);
        var result = new List<CommentResponse>(items.Count);
        foreach (var comment in items)
        {
            var history = await ListRevisionsAsync(
                organizationId, projectId, workItemId, comment.Id, 1, 200, ct);
            result.Add(new CommentResponse(
                comment.Id, comment.Body, comment.AuthorUserId, comment.Mentions,
                comment.CreatedAt, comment.EditedAt, history.Items));
        }
        return new(result, normalized.Page, normalized.PageSize,
            await comments.CountByFilterAsync(filter, ct));
    }

    public async Task<WorkItemActivityPage<CommentRevisionResponse>> ListRevisionsAsync(
        string organizationId, string projectId, string workItemId, string commentId,
        int page, int pageSize, CancellationToken ct)
    {
        var normalized = NormalizePage(page, pageSize);
        var items = await revisions.ListByFilterAsync(
            x => x.OrganizationId == organizationId && x.ProjectId == projectId
                && x.WorkItemId == workItemId && x.CommentId == commentId,
            x => x.EditedAt, page: normalized.Page, pageSize: normalized.PageSize, cancellationToken: ct);
        var count = await revisions.CountByFilterAsync(
            x => x.OrganizationId == organizationId && x.ProjectId == projectId
                && x.WorkItemId == workItemId && x.CommentId == commentId, ct);
        return new(items.Select(x => new CommentRevisionResponse(x.Body, x.EditedByUserId, x.EditedAt)).ToList(),
            normalized.Page, normalized.PageSize, count);
    }

    public Task<WorkItemActivityPage<AttachmentResponse>> ListAttachmentsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct) =>
        PageAsync(attachments,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId,
            x => x.CreatedAt,
            x => new AttachmentResponse(
                x.Id, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt,
                x.SecurityState, x.ScanProvider, x.ScannedAt),
            page, pageSize, ct);

    public async Task<IReadOnlyDictionary<string, WorkItemUserActivityReference>> FindUserReferencesAsync(
        string organizationId,
        string userId,
        CancellationToken ct)
    {
        var commentData = await LoadAllAsync(comments,
            x => x.OrganizationId == organizationId
                && (x.AuthorUserId == userId || x.Mentions.Contains(userId)), ct);
        var revisionData = await LoadAllAsync(revisions,
            x => x.OrganizationId == organizationId && x.EditedByUserId == userId, ct);
        var workLogData = await LoadAllAsync(workLogs,
            x => x.OrganizationId == organizationId && x.UserId == userId, ct);
        var approvalData = await LoadAllAsync(approvals,
            x => x.OrganizationId == organizationId
                && (x.RequestedByUserId == userId || x.DecidedByUserId == userId), ct);
        var timelineData = await LoadAllAsync(timeline,
            x => x.OrganizationId == organizationId && x.ChangedByUserId == userId, ct);
        var ids = commentData.Select(x => x.WorkItemId)
            .Concat(revisionData.Select(x => x.WorkItemId))
            .Concat(workLogData.Select(x => x.WorkItemId))
            .Concat(approvalData.Select(x => x.WorkItemId))
            .Concat(timelineData.Select(x => x.WorkItemId))
            .Distinct(StringComparer.Ordinal);
        return ids.ToDictionary(
            id => id,
            id => new WorkItemUserActivityReference(
                id,
                commentData.Any(x => x.WorkItemId == id && x.AuthorUserId == userId),
                revisionData.Any(x => x.WorkItemId == id),
                commentData.Any(x => x.WorkItemId == id && x.Mentions.Contains(userId)),
                workLogData.Any(x => x.WorkItemId == id),
                approvalData.Any(x => x.WorkItemId == id),
                timelineData.Any(x => x.WorkItemId == id)),
            StringComparer.Ordinal);
    }

    public async IAsyncEnumerable<WorkItemUserActivityReference> StreamUserReferencesAsync(
        string organizationId,
        string userId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var item in StreamAsync(
            comments,
            x => x.OrganizationId == organizationId
                && (x.AuthorUserId == userId || x.Mentions.Contains(userId)),
            ct))
        {
            yield return new WorkItemUserActivityReference(
                item.WorkItemId,
                item.AuthorUserId == userId,
                false,
                item.Mentions.Contains(userId),
                false,
                false,
                false);
        }
        await foreach (var item in StreamAsync(
            revisions,
            x => x.OrganizationId == organizationId && x.EditedByUserId == userId,
            ct))
        {
            yield return new WorkItemUserActivityReference(
                item.WorkItemId, false, true, false, false, false, false);
        }
        await foreach (var item in StreamAsync(
            workLogs,
            x => x.OrganizationId == organizationId && x.UserId == userId,
            ct))
        {
            yield return new WorkItemUserActivityReference(
                item.WorkItemId, false, false, false, true, false, false);
        }
        await foreach (var item in StreamAsync(
            approvals,
            x => x.OrganizationId == organizationId
                && (x.RequestedByUserId == userId || x.DecidedByUserId == userId),
            ct))
        {
            yield return new WorkItemUserActivityReference(
                item.WorkItemId, false, false, false, false, true, false);
        }
        await foreach (var item in StreamAsync(
            timeline,
            x => x.OrganizationId == organizationId && x.ChangedByUserId == userId,
            ct))
        {
            yield return new WorkItemUserActivityReference(
                item.WorkItemId, false, false, false, false, false, true);
        }
    }

    public async Task AnonymizeUserReferencesAsync(
        string organizationId,
        string userId,
        string pseudonym,
        CancellationToken ct)
    {
        var commentData = await LoadAllAsync(comments,
            x => x.OrganizationId == organizationId
                && (x.AuthorUserId == userId || x.Mentions.Contains(userId)), ct);
        foreach (var comment in commentData)
        {
            if (comment.AuthorUserId == userId) comment.AuthorUserId = pseudonym;
            comment.Mentions.RemoveAll(x => x == userId);
            await ReplaceOwnedAsync(comments, comment, ct);
        }

        var revisionData = await LoadAllAsync(revisions,
            x => x.OrganizationId == organizationId && x.EditedByUserId == userId, ct);
        foreach (var revision in revisionData)
        {
            revision.EditedByUserId = pseudonym;
            await ReplaceOwnedAsync(revisions, revision, ct);
        }

        var workLogData = await LoadAllAsync(workLogs,
            x => x.OrganizationId == organizationId && x.UserId == userId, ct);
        foreach (var workLog in workLogData)
        {
            workLog.UserId = pseudonym;
            await ReplaceOwnedAsync(workLogs, workLog, ct);
        }

        var approvalData = await LoadAllAsync(approvals,
            x => x.OrganizationId == organizationId
                && (x.RequestedByUserId == userId || x.DecidedByUserId == userId), ct);
        foreach (var approval in approvalData)
        {
            if (approval.RequestedByUserId == userId) approval.RequestedByUserId = pseudonym;
            if (approval.DecidedByUserId == userId) approval.DecidedByUserId = pseudonym;
            await ReplaceOwnedAsync(approvals, approval, ct);
        }

        var timelineData = await LoadAllAsync(timeline,
            x => x.OrganizationId == organizationId && x.ChangedByUserId == userId, ct);
        foreach (var entry in timelineData)
        {
            entry.ChangedByUserId = pseudonym;
            await ReplaceOwnedAsync(timeline, entry, ct);
        }
    }

    public Task<WorkItemActivityPage<WorkLogResponse>> ListWorkLogsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct) =>
        PageAsync(workLogs,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId,
            x => x.CreatedAt,
            x => new WorkLogResponse(x.Id, x.UserId, x.Hours, x.Note, x.CreatedAt),
            page, pageSize, ct);

    public Task<WorkItemActivityPage<WorkItemApprovalResponse>> ListApprovalsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct) =>
        PageAsync(approvals,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId,
            x => x.RequestedAt,
            x => new WorkItemApprovalResponse(x.Id, x.FromStatus, x.ToStatus, x.RequestedByUserId,
                x.RequestedAt, x.ExpiresAt, x.Status, x.DecidedByUserId, x.DecidedAt, x.Note, x.ConsumedAt),
            page, pageSize, ct);

    public Task<WorkItemActivityPage<WorkItemStatusHistoryResponse>> ListTimelineAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct) =>
        PageAsync(timeline,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId,
            x => x.ChangedAt,
            x => new WorkItemStatusHistoryResponse(x.FromStatus, x.ToStatus, x.ChangedByUserId, x.ChangedAt),
            page, pageSize, ct);

    private static async Task CreateOwnedAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        TDocument document,
        CancellationToken ct)
        where TDocument : class, IWorkItemActivityDocument
    {
        ValidateActivityOwnership(document);
        await repository.CreateAsync(document, ct);
    }

    private static async Task ReplaceOwnedAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        TDocument document,
        CancellationToken ct)
        where TDocument : class, IWorkItemActivityDocument
    {
        ValidateActivityOwnership(document);
        var result = await repository.ReplaceByVersionAsync(x => x.Id == document.Id, document, document.Version, ct);
        if (!result.Found)
        {
            throw new NotFoundException("WORK_ITEM_ACTIVITY_NOT_FOUND", "Work item activity was not found.");
        }
        document.Version = result.Version!.Value;
    }

    private static async Task CreateOrValidateAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        TDocument document,
        Func<TDocument, bool> compatible,
        CancellationToken ct)
        where TDocument : class, IVersionedDocument
    {
        try
        {
            await repository.CreateAsync(document, ct);
        }
        catch (DocumentConflictException)
        {
            var existing = await repository.SelectAsync(x => x.Id == document.Id, ct);
            if (existing is null || !compatible(existing))
            {
                throw new ConflictException(
                    "WORK_ITEM_ACTIVITY_MIGRATION_CONFLICT",
                    "Legacy work item activity conflicts with an existing activity record.");
            }
        }
    }

    private static async IAsyncEnumerable<TDocument> StreamAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        [EnumeratorCancellation] CancellationToken ct)
        where TDocument : class, IDocument
    {
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            foreach (var item in page.Items) yield return item;
            cursor = page.NextCursor;
        }
        while (cursor is not null);
    }

    private static async Task<IReadOnlyList<TDocument>> LoadAllAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        System.Linq.Expressions.Expression<Func<TDocument, bool>> filter,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var result = new List<TDocument>();
        string? cursor = null;
        do
        {
            var current = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            result.AddRange(current.Items);
            cursor = current.NextCursor;
        }
        while (cursor is not null);
        return result;
    }

    private static async Task<WorkItemActivityPage<TResponse>> PageAsync<TDocument, TOrder, TResponse>(
        IDocumentRepository<TDocument> repository,
        System.Linq.Expressions.Expression<Func<TDocument, bool>> filter,
        System.Linq.Expressions.Expression<Func<TDocument, TOrder>> orderBy,
        Func<TDocument, TResponse> map,
        int page,
        int pageSize,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var normalized = NormalizePage(page, pageSize);
        var items = await repository.ListByFilterAsync(
            filter,
            BoxOrder(orderBy),
            page: normalized.Page,
            pageSize: normalized.PageSize,
            cancellationToken: ct);
        return new(items.Select(map).ToList(), normalized.Page, normalized.PageSize,
            await repository.CountByFilterAsync(filter, ct));
    }

    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, 100));

    private static Expression<Func<TDocument, object>> BoxOrder<TDocument, TOrder>(
        Expression<Func<TDocument, TOrder>> expression)
    {
        var body = expression.Body.Type == typeof(object)
            ? expression.Body
            : Expression.Convert(expression.Body, typeof(object));
        return Expression.Lambda<Func<TDocument, object>>(body, expression.Parameters);
    }

    private static void ValidateActivityOwnership(IWorkItemActivityDocument document) =>
        ValidateOwnership(document.OrganizationId, document.ProjectId, document.WorkItemId);

    private static void ValidateOwnership(string? organizationId, string? projectId, string? workItemId)
    {
        if (string.IsNullOrWhiteSpace(organizationId)
            || string.IsNullOrWhiteSpace(projectId)
            || string.IsNullOrWhiteSpace(workItemId))
        {
            throw new InvalidOperationException("Work item activity tenant, project and work-item ownership are required.");
        }
    }

    private static bool SameOwner(
        WorkItemCommentActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemCommentRevisionActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemAttachmentActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemWorkLogActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemApprovalActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemTimelineActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;

    private static bool SamePayload<TDocument>(TDocument stored, TDocument expected)
        where TDocument : class, IVersionedDocument
    {
        var storedNode = JsonSerializer.SerializeToNode(stored)?.AsObject();
        var expectedNode = JsonSerializer.SerializeToNode(expected)?.AsObject();
        storedNode?.Remove(nameof(IVersionedDocument.Version));
        expectedNode?.Remove(nameof(IVersionedDocument.Version));
        return JsonNode.DeepEquals(storedNode, expectedNode);
    }

    internal static WorkItemCommentActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, CommentDocument source) => new()
    {
        Id = source.Id,
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        Body = source.Body,
        AuthorUserId = source.AuthorUserId,
        Mentions = [.. source.Mentions],
        CreatedAt = source.CreatedAt,
        EditedAt = source.EditedAt
    };

    internal static WorkItemCommentRevisionActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, string commentId, CommentRevisionDocument source, int ordinal) => new()
    {
        Id = DeterministicId("revision", item.Id, commentId, ordinal.ToString(), source.EditedAt.ToUniversalTime().Ticks.ToString()),
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        CommentId = commentId,
        Body = source.Body,
        EditedByUserId = source.EditedByUserId,
        EditedAt = source.EditedAt
    };

    internal static WorkItemAttachmentActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, AttachmentDocument source) => new()
    {
        Id = source.Id,
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        FileName = source.FileName,
        ContentType = source.ContentType,
        SizeBytes = source.SizeBytes,
            StoragePath = source.StoragePath,
            ChecksumSha256 = source.ChecksumSha256,
            SecurityState = source.SecurityState,
            ScanProvider = source.ScanProvider,
            ScanDetail = source.ScanDetail,
            ScannedAt = source.ScannedAt,
            CreatedAt = source.CreatedAt
    };

    internal static WorkItemWorkLogActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, WorkLogDocument source) => new()
    {
        Id = source.Id,
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        UserId = source.UserId,
        Hours = source.Hours,
        Note = source.Note,
        CreatedAt = source.CreatedAt
    };

    internal static WorkItemApprovalActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, WorkItemApprovalDocument source) => new()
    {
        Id = source.Id,
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        FromStatus = source.FromStatus,
        ToStatus = source.ToStatus,
        RequestedByUserId = source.RequestedByUserId,
        RequestedAt = source.RequestedAt,
        ExpiresAt = source.ExpiresAt,
        Status = source.Status,
        DecidedByUserId = source.DecidedByUserId,
        DecidedAt = source.DecidedAt,
        Note = source.Note,
        ConsumedAt = source.ConsumedAt
    };

    internal static WorkItemTimelineActivityDocument ToActivity(
        WorkItemDocument item, string organizationId, WorkItemStatusHistoryDocument source, int ordinal) => new()
    {
        Id = DeterministicId("timeline", item.Id, ordinal.ToString(), source.ChangedAt.ToUniversalTime().Ticks.ToString(), source.ToStatus),
        OrganizationId = organizationId,
        ProjectId = item.ProjectId,
        WorkItemId = item.Id,
        FromStatus = source.FromStatus,
        ToStatus = source.ToStatus,
        ChangedByUserId = source.ChangedByUserId,
        ChangedAt = source.ChangedAt
    };

    private static AttachmentDocument ToEmbedded(WorkItemAttachmentActivityDocument source) => new()
    {
        Id = source.Id,
        FileName = source.FileName,
        ContentType = source.ContentType,
        SizeBytes = source.SizeBytes,
            StoragePath = source.StoragePath,
            ChecksumSha256 = source.ChecksumSha256,
            SecurityState = source.SecurityState,
            ScanProvider = source.ScanProvider,
            ScanDetail = source.ScanDetail,
            ScannedAt = source.ScannedAt,
            CreatedAt = source.CreatedAt
    };

    private static WorkLogDocument ToEmbedded(WorkItemWorkLogActivityDocument source) => new()
    {
        Id = source.Id,
        UserId = source.UserId,
        Hours = source.Hours,
        Note = source.Note,
        CreatedAt = source.CreatedAt
    };

    private static WorkItemApprovalDocument ToEmbedded(WorkItemApprovalActivityDocument source) => new()
    {
        Id = source.Id,
        FromStatus = source.FromStatus,
        ToStatus = source.ToStatus,
        RequestedByUserId = source.RequestedByUserId,
        RequestedAt = source.RequestedAt,
        ExpiresAt = source.ExpiresAt,
        Status = source.Status,
        DecidedByUserId = source.DecidedByUserId,
        DecidedAt = source.DecidedAt,
        Note = source.Note,
        ConsumedAt = source.ConsumedAt
    };

    private static WorkItemStatusHistoryDocument ToEmbedded(WorkItemTimelineActivityDocument source) => new()
    {
        FromStatus = source.FromStatus,
        ToStatus = source.ToStatus,
        ChangedByUserId = source.ChangedByUserId,
        ChangedAt = source.ChangedAt
    };

    private static string DeterministicId(params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }
}
