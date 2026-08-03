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

public sealed partial class WorkItemActivityStore{

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
}
