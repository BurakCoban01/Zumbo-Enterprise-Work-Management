using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class OpenAttachmentHandler(WorkItemService service)
{
    private OpenAttachmentSlice? slice;

    public OpenAttachmentHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemActivityStore activityStore,
        IAttachmentStorage attachmentStorage)
        : this(null!)
    {
        slice = new OpenAttachmentSlice(
            new OpenAttachmentPipeline(
                workItems,
                currentUser,
                permissionChecker,
                activityStore,
                attachmentStorage));
    }

    public Task<AttachmentFile> HandleAsync(OpenAttachmentQuery query, CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.OpenAttachmentAsync(query.Id, query.AttachmentId, ct);
}
