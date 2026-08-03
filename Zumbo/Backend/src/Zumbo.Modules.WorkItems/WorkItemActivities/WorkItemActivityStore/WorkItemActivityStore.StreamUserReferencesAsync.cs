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
}
