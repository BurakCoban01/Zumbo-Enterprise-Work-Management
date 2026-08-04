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
