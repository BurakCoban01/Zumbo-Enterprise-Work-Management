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
