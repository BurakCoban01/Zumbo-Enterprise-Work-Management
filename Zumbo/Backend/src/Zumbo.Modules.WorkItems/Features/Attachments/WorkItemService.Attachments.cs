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
    {
        const long maxSizeBytes = 25 * 1024 * 1024;
        if (declaredSizeBytes is <= 0 or > maxSizeBytes)
        {
            throw new ValidationException("Attachment size must be between 1 byte and 25 MB.");
        }

        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 180)
        {
            throw new ValidationException("Attachment file name is required and cannot exceed 180 characters.");
        }

        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "AttachmentCreate", ct);
        await EnsureSeparatedAsync(workItem, ct);
        if (workItem.Attachments.Count >= 100)
        {
            throw new ConflictException("ATTACHMENT_LIMIT_REACHED", "A work item cannot contain more than 100 attachments.");
        }

        var stored = await attachmentStorage.SaveAsync(content, fileName, contentType, maxSizeBytes, ct);
        var attachment = new AttachmentDocument
        {
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            StoragePath = stored.StoragePath,
            ChecksumSha256 = stored.ChecksumSha256,
            SecurityState = stored.SecurityState,
            ScanProvider = stored.ScanProvider,
            ScanDetail = stored.ScanDetail,
            ScannedAt = stored.ScannedAt,
            CreatedAt = clock.UtcNow
        };
        workItem.Attachments.Add(attachment);
        try
        {
            await activityStore.CreateAttachmentAsync(
                WorkItemActivityStore.ToActivity(workItem, CurrentOrganizationId(workItem.ProjectId), attachment),
                ct);
        }
        catch
        {
            var cleanup = await CompensationExecution.RunAsync(
                "work_item.attachment.delete",
                token => attachmentStorage.DeleteAsync(stored.StoragePath, token));
            ObserveCompensation(cleanup);
            throw;
        }

        await audit.WriteAsync(
            "WorkItemAttachmentUploaded",
            "WorkItem",
            workItem.Id,
            null,
            $"{attachment.Id}:{attachment.FileName}:{attachment.SizeBytes}",
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemAttachmentUploaded", "Attachment uploaded", attachment.Id, ct);
        return ToResponse(workItem);
    }

    public async Task<AttachmentFile> OpenAttachmentAsync(string id, string attachmentId, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemView", ct);
        if (workItem.ActivityStorageVersion < 1)
        {
            var legacy = workItem.Attachments.SingleOrDefault(x => x.Id == attachmentId)
                ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
            EnsureAttachmentIsClean(legacy.SecurityState);
            var legacyContent = await attachmentStorage.OpenReadAsync(
                legacy.StoragePath, legacy.ContentType, legacy.ChecksumSha256, ct);
            return new AttachmentFile(legacyContent, legacy.FileName, legacy.ContentType, legacy.SizeBytes);
        }

        var attachment = await activityStore.GetAttachmentAsync(
            CurrentOrganizationId(workItem.ProjectId), workItem.ProjectId, workItem.Id, attachmentId, ct)
            ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
        EnsureAttachmentIsClean(attachment.SecurityState);
        var content = await attachmentStorage.OpenReadAsync(
            attachment.StoragePath, attachment.ContentType, attachment.ChecksumSha256, ct);
        return new AttachmentFile(content, attachment.FileName, attachment.ContentType, attachment.SizeBytes);
    }

    public async Task<WorkItemResponse> DeleteAttachmentAsync(
        string id,
        string attachmentId,
        string correlationId,
        CancellationToken ct)
    {
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "AttachmentDelete", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var attachment = await activityStore.GetAttachmentAsync(
            CurrentOrganizationId(workItem.ProjectId), workItem.ProjectId, workItem.Id, attachmentId, ct)
            ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
        await activityStore.DeleteAttachmentAsync(attachment, ct);
        workItem.Attachments.RemoveAll(x => x.Id == attachment.Id);
        try
        {
            await attachmentStorage.DeleteAsync(attachment.StoragePath, ct);
        }
        catch
        {
            var restore = await CompensationExecution.RunAsync(
                "work_item.attachment.restore",
                token => activityStore.CreateAttachmentAsync(attachment, token));
            ObserveCompensation(restore);
            workItem.Attachments.Add(new AttachmentDocument
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes,
                StoragePath = attachment.StoragePath,
                ChecksumSha256 = attachment.ChecksumSha256,
                SecurityState = attachment.SecurityState,
                ScanProvider = attachment.ScanProvider,
                ScanDetail = attachment.ScanDetail,
                ScannedAt = attachment.ScannedAt,
                CreatedAt = attachment.CreatedAt
            });
            throw;
        }

        await audit.WriteAsync(
            "WorkItemAttachmentDeleted",
            "WorkItem",
            workItem.Id,
            $"{attachment.Id}:{attachment.FileName}:{attachment.SizeBytes}",
            null,
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemAttachmentDeleted", "Attachment deleted", correlationId, ct);
        return ToResponse(workItem);
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
