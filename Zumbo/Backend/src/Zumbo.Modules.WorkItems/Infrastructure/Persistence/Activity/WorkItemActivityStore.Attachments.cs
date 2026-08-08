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
    public Task CreateAttachmentAsync(WorkItemAttachmentActivityDocument attachment, CancellationToken ct) =>
        CreateOwnedAsync(attachments, attachment, ct);

    public async Task DeleteAttachmentAsync(WorkItemAttachmentActivityDocument attachment, CancellationToken ct)
    {
        ValidateOwnership(attachment.OrganizationId, attachment.ProjectId, attachment.WorkItemId);
        var deleted = await attachments.DeleteByFilterAsync(x => x.Id == attachment.Id
            && x.OrganizationId == attachment.OrganizationId
            && x.ProjectId == attachment.ProjectId
            && x.WorkItemId == attachment.WorkItemId
            && x.Version == attachment.Version, ct);
        if (deleted == 0)
        {
            throw new ConflictException("ATTACHMENT_CONCURRENTLY_CHANGED", "Attachment changed before it could be deleted.");
        }
    }

    public Task<WorkItemAttachmentActivityDocument?> GetAttachmentAsync(
        string organizationId, string projectId, string workItemId, string attachmentId, CancellationToken ct) =>
        attachments.SelectAsync(x => x.Id == attachmentId
            && x.OrganizationId == organizationId
            && x.ProjectId == projectId
            && x.WorkItemId == workItemId, ct);

    public Task<WorkItemActivityPage<AttachmentResponse>> ListAttachmentsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct) =>
        PageAsync(attachments,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId,
            x => x.CreatedAt,
            x => new AttachmentResponse(
                x.Id, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt,
                x.SecurityState, x.ScanProvider, x.ScannedAt),
            page, pageSize, ct);
}
