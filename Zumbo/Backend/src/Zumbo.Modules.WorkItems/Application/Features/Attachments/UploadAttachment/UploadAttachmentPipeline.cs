using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class UploadAttachmentPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemAuditPublisher audit,
    IClock clock,
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

    internal async Task<WorkItemResponse> UploadAsync(
        UploadAttachmentCommand command,
        long maxSizeBytes,
        CancellationToken ct)
    {
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + command.Id, ct);
        var workItem = await GetWorkItemAsync(command.Id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.AttachmentCreate, ct);
        await EnsureSeparatedAsync(workItem, ct);
        if (workItem.Attachments.Count >= 100)
        {
            throw new ConflictException(
                "ATTACHMENT_LIMIT_REACHED",
                "A work item cannot contain more than 100 attachments.");
        }

        var stored = await attachmentStorage.SaveAsync(
            command.Content,
            command.FileName,
            command.ContentType,
            maxSizeBytes,
            ct);
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
                WorkItemActivityStore.ToActivity(
                    workItem,
                    CurrentOrganizationId(workItem.ProjectId),
                    attachment),
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
            command.CorrelationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(workItem, attachment.Id, ct);
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
        string attachmentId,
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
            "WorkItemAttachmentUploaded",
            "Attachment uploaded",
            attachmentId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title}: Attachment uploaded",
            attachmentId,
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
