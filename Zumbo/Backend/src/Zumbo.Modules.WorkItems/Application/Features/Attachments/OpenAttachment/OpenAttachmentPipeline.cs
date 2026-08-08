using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class OpenAttachmentPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemActivityStore activityStore,
    IAttachmentStorage attachmentStorage)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);

    internal async Task<AttachmentFile> OpenAsync(
        string id,
        string attachmentId,
        CancellationToken ct)
    {
        var workItem = await GetWorkItemAsync(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemView, ct);
        if (workItem.ActivityStorageVersion < 1)
        {
            var legacy = workItem.Attachments.SingleOrDefault(item => item.Id == attachmentId)
                ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
            EnsureAttachmentIsClean(legacy.SecurityState);
            var legacyContent = await attachmentStorage.OpenReadAsync(
                legacy.StoragePath,
                legacy.ContentType,
                legacy.ChecksumSha256,
                ct);
            return new AttachmentFile(
                legacyContent,
                legacy.FileName,
                legacy.ContentType,
                legacy.SizeBytes);
        }

        var attachment = await activityStore.GetAttachmentAsync(
            CurrentOrganizationId(workItem.ProjectId),
            workItem.ProjectId,
            workItem.Id,
            attachmentId,
            ct)
            ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
        EnsureAttachmentIsClean(attachment.SecurityState);
        var content = await attachmentStorage.OpenReadAsync(
            attachment.StoragePath,
            attachment.ContentType,
            attachment.ChecksumSha256,
            ct);
        return new AttachmentFile(
            content,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes);
    }

    private async Task<WorkItemDocument> GetWorkItemAsync(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(item => item.Id == id && !item.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(
            workItem.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        return workItem;
    }

    private async Task<ProjectResourceAuthorization> EnsurePermissionAsync(
        string projectId,
        string permission,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var authorization = await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
        authorizedOrganizationIds[projectId] = authorization.OrganizationId;
        return authorization;
    }

    private string CurrentOrganizationId(string projectId)
    {
        if (!authorizedOrganizationIds.TryGetValue(projectId, out var organizationId))
        {
            throw new InvalidOperationException(
                "Project resource must be authorized before tenant data is accessed.");
        }

        return organizationId;
    }

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
