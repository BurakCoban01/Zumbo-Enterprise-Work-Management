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

public sealed partial class WorkItemActivityStore
{
    public Task CreateCommentAsync(WorkItemCommentActivityDocument comment, CancellationToken ct) =>
        CreateOwnedAsync(comments, comment, ct);

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

    public Task<WorkItemCommentActivityDocument?> GetCommentAsync(
        string organizationId, string projectId, string workItemId, string commentId, CancellationToken ct) =>
        comments.SelectAsync(x => x.Id == commentId
            && x.OrganizationId == organizationId
            && x.ProjectId == projectId
            && x.WorkItemId == workItemId, ct);

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

    public Task CreateRevisionAsync(WorkItemCommentRevisionActivityDocument revision, CancellationToken ct) =>
        CreateOwnedAsync(revisions, revision, ct);

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
}
