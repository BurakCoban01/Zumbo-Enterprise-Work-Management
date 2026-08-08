using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class DeleteAttachmentPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemAuditPublisher audit,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IAttachmentStorage attachmentStorage,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IWorkItemActivityStore activityStore,
    IExpectedVersionAccessor? expectedVersions,
    WorkItemCollaborationService? collaborationService,
    ILogger<WorkItemService>? logger)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal async Task<WorkItemResponse> DeleteAsync(
        DeleteAttachmentCommand command,
        CancellationToken ct)
    {
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + command.Id, ct);
        var workItem = await GetWorkItemAsync(command.Id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.AttachmentDelete, ct);
        await EnsureSeparatedAsync(workItem, ct);
        var attachment = await activityStore.GetAttachmentAsync(
            CurrentOrganizationId(workItem.ProjectId),
            workItem.ProjectId,
            workItem.Id,
            command.AttachmentId,
            ct)
            ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
        await activityStore.DeleteAttachmentAsync(attachment, ct);
        workItem.Attachments.RemoveAll(item => item.Id == attachment.Id);
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
            command.CorrelationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(workItem, command.CorrelationId, ct);
        return WorkItemResponseMapper.ToResponse(workItem);
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

    private async Task<IAsyncDisposable> AcquireRequiredLockAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        var leaseTime = TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300));
        var waitTime = TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30));
        return await distributedLockProvider.TryAcquireAsync(resource, leaseTime, waitTime, ct)
            ?? throw new ConflictException(
                "RESOURCE_BUSY",
                "The requested resource is busy; retry the operation.");
    }

    private async Task EnsureSeparatedAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        if (workItem.ActivityStorageVersion >= 1)
        {
            return;
        }

        await activityStore.MigrateEmbeddedAsync(
            workItem,
            CurrentOrganizationId(workItem.ProjectId),
            ct);
        await SaveAsync(workItem, ct);
    }

    private async Task SaveAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        await activityStore.MigrateEmbeddedAsync(
            workItem,
            CurrentOrganizationId(workItem.ProjectId),
            ct);
        var comments = workItem.Comments;
        var attachments = workItem.Attachments;
        var workLogs = workItem.WorkLogs;
        var approvals = workItem.Approvals;
        var statusHistory = workItem.StatusHistory;
        workItem.Comments = [];
        workItem.Attachments = [];
        workItem.WorkLogs = [];
        workItem.Approvals = [];
        workItem.StatusHistory = [];
        try
        {
            var result = await workItems.ReplaceByVersionAsync(
                item => item.Id == workItem.Id,
                workItem,
                expectedVersion.Consume(workItem.Version),
                ct);
            if (!result.Found)
            {
                throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
            }

            workItem.Version = result.Version!.Value;
        }
        finally
        {
            workItem.Comments = comments;
            workItem.Attachments = attachments;
            workItem.WorkLogs = workLogs;
            workItem.Approvals = approvals;
            workItem.StatusHistory = statusHistory;
        }
    }

    private async Task RecordActivityAndNotifyWatchersAsync(
        WorkItemDocument workItem,
        string correlationId,
        CancellationToken ct)
    {
        if (collaborationService is null)
        {
            return;
        }

        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        await collaborationService.RecordActivityAsync(
            workItem,
            organizationId,
            "WorkItemAttachmentDeleted",
            "Attachment deleted",
            correlationId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title}: Attachment deleted",
            correlationId,
            null,
            ct);
    }

    private void ObserveCompensation(CompensationResult result)
    {
        if (!result.Succeeded)
        {
            logger?.LogWarning(
                "Compensation operation {Operation} ended with {Outcome}; failure type {FailureType}.",
                result.Operation,
                result.Outcome,
                result.Exception?.GetType().Name ?? "none");
        }
    }
}
