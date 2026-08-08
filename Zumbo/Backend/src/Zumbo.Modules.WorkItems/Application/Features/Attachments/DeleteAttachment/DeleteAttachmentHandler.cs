using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class DeleteAttachmentHandler(WorkItemService service)
{
    private DeleteAttachmentPipeline? pipeline;

    public DeleteAttachmentHandler(
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
        : this(null!)
    {
        pipeline = new DeleteAttachmentPipeline(
            workItems,
            audit,
            currentUser,
            permissionChecker,
            attachmentStorage,
            distributedLockProvider,
            distributedLockOptions,
            activityStore,
            expectedVersions,
            collaborationService,
            logger);
    }

    public Task<WorkItemResponse> HandleAsync(DeleteAttachmentCommand command, CancellationToken ct) =>
        pipeline?.DeleteAsync(command, ct)
        ?? service.DeleteAttachmentAsync(
            command.Id,
            command.AttachmentId,
            command.CorrelationId,
            ct);
}
