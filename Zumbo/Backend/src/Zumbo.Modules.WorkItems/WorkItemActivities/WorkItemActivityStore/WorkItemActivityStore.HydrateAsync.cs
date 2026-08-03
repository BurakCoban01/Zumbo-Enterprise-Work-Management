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

    public async Task HydrateAsync(WorkItemDocument workItem, string organizationId, CancellationToken ct)
    {
        ValidateOwnership(organizationId, workItem.ProjectId, workItem.Id);
        if (workItem.ActivityStorageVersion < 1)
        {
            return;
        }

        var storedComments = await LoadAllAsync(
            comments,
            x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
            ct);
        var storedRevisions = await LoadAllAsync(
            revisions,
            x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
            ct);
        workItem.Comments = storedComments
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(comment => new CommentDocument
            {
                Id = comment.Id,
                Body = comment.Body,
                AuthorUserId = comment.AuthorUserId,
                Mentions = [.. comment.Mentions],
                CreatedAt = comment.CreatedAt,
                EditedAt = comment.EditedAt,
                History = storedRevisions
                    .Where(x => x.CommentId == comment.Id)
                    .OrderBy(x => x.EditedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
                    .Select(x => new CommentRevisionDocument
                    {
                        Body = x.Body,
                        EditedByUserId = x.EditedByUserId,
                        EditedAt = x.EditedAt
                    }).ToList()
            }).ToList();

        workItem.Attachments = (await LoadAllAsync(
                attachments,
                x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
                ct))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(ToEmbedded).ToList();
        workItem.WorkLogs = (await LoadAllAsync(
                workLogs,
                x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
                ct))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(ToEmbedded).ToList();
        workItem.Approvals = (await LoadAllAsync(
                approvals,
                x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
                ct))
            .OrderBy(x => x.RequestedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(ToEmbedded).ToList();
        workItem.StatusHistory = (await LoadAllAsync(
                timeline,
                x => x.OrganizationId == organizationId && x.ProjectId == workItem.ProjectId && x.WorkItemId == workItem.Id,
                ct))
            .OrderBy(x => x.ChangedAt).ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(ToEmbedded).ToList();
    }
}
