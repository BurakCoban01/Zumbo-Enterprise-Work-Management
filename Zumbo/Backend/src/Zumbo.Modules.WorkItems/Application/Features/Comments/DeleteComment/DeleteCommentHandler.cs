using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class DeleteCommentHandler(WorkItemService service)
{
    private DeleteCommentSlice? slice;

    public DeleteCommentHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemAuditPublisher audit,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService)
        : this(null!)
    {
        slice = new DeleteCommentSlice(
            new DeleteCommentPipeline(
                workItems,
                audit,
                currentUser,
                permissionChecker,
                activityStore,
                expectedVersions,
                collaborationService));
    }

    public Task<WorkItemResponse> HandleAsync(DeleteCommentCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.DeleteCommentAsync(command.Id, command.CommentId, command.CorrelationId, ct);
}
