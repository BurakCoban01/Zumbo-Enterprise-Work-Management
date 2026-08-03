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
}
