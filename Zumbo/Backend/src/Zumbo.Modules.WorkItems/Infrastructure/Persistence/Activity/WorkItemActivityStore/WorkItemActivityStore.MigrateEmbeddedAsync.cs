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
}
