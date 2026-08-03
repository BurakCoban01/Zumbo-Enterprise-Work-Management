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
}
