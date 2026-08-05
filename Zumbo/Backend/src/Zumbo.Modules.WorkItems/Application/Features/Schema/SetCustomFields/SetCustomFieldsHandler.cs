using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class SetCustomFieldsHandler(WorkItemService service)
{
    private SetCustomFieldsSlice? slice;

    public SetCustomFieldsHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemTypeSchemaPolicy typeSchemas,
        IWorkItemSearchPublisher searchPublisher,
        IWorkItemRealtimePublisher realtimePublisher,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
        IWorkItemActivityStore activityStore,
        IExpectedVersionAccessor? expectedVersions,
        WorkItemCollaborationService? collaborationService,
        IWorkItemAutomationEventPublisher? automationEvents,
        IWorkItemAutomationChainContextAccessor? automationChain)
        : this(null!)
    {
        slice = new SetCustomFieldsSlice(
            new CustomFieldMutationPipeline(
                workItems,
                audit,
                clock,
                currentUser,
                permissionChecker,
                searchPublisher,
                realtimePublisher,
                cacheInvalidationPublisher,
                activityStore,
                expectedVersions,
                collaborationService,
                automationEvents,
                automationChain),
            typeSchemas);
    }

    public Task<WorkItemResponse> HandleAsync(SetCustomFieldsCommand command, CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.SetCustomFieldsAsync(command.Id, command.Request, command.CorrelationId, ct);
}
