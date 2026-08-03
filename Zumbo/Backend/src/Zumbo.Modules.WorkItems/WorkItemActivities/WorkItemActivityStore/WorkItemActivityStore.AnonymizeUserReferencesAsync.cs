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
}
