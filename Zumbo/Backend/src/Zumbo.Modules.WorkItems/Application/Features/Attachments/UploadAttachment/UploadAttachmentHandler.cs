using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class UploadAttachmentHandler(WorkItemService service)
{
    private UploadAttachmentSlice? slice;

    public UploadAttachmentHandler(
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
        : this(null!)
    {
        slice = new UploadAttachmentSlice(
            new UploadAttachmentPipeline(
                workItems,
                audit,
                clock,
                currentUser,
                permissionChecker,
                attachmentStorage,
                distributedLockProvider,
                distributedLockOptions,
                activityStore,
                expectedVersions,
                collaborationService,
                logger));
    }

    public Task<WorkItemResponse> HandleAsync(UploadAttachmentCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.UploadAttachmentAsync(
            command.Id,
            command.Content,
            command.FileName,
            command.ContentType,
            command.DeclaredSizeBytes,
            command.CorrelationId,
            ct);
}
