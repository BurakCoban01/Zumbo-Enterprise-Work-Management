using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class EditCommentHandler(WorkItemService service)
{
    private EditCommentSlice? slice;

    public EditCommentHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new EditCommentSlice(
            new EditCommentPipeline(
                workItems,
                audit,
                clock,
                currentUser,
                permissionChecker,
                activityStore,
                expectedVersions,
                collaborationService));
    }

    public Task<WorkItemResponse> HandleAsync(EditCommentCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.EditCommentAsync(
            command.Id,
            command.CommentId,
            command.Request,
            command.CorrelationId,
            ct);
}
