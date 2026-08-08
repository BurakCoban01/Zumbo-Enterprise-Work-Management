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
}
