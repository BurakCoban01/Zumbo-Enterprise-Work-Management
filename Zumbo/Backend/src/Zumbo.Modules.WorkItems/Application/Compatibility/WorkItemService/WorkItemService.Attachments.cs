using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    public async Task<WorkItemResponse> UploadAttachmentAsync(
        string id,
        Stream content,
        string fileName,
        string contentType,
        long declaredSizeBytes,
        string correlationId,
        CancellationToken ct)
        => await uploadAttachmentHandler.HandleAsync(
            new UploadAttachmentCommand(
                id,
                content,
                fileName,
                contentType,
                declaredSizeBytes,
                correlationId),
            ct);

    public async Task<AttachmentFile> OpenAttachmentAsync(string id, string attachmentId, CancellationToken ct)
        => await openAttachmentHandler.HandleAsync(new OpenAttachmentQuery(id, attachmentId), ct);

    public async Task<WorkItemResponse> DeleteAttachmentAsync(
        string id,
        string attachmentId,
        string correlationId,
        CancellationToken ct)
        => await deleteAttachmentHandler.HandleAsync(
            new DeleteAttachmentCommand(id, attachmentId, correlationId),
            ct);

    private static void EnsureAttachmentIsClean(string securityState)
    {
        if (!securityState.Equals(AttachmentSecurityStates.Clean, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "ATTACHMENT_NOT_CLEAN",
                "Attachment content is not available until security scanning completes successfully.");
        }
    }
}
