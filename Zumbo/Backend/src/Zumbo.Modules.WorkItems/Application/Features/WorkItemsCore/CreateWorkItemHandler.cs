using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Features.WorkItemsCore;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class CreateWorkItemHandler(WorkItemService service)
{
    private CreateWorkItemSlice? slice;

    public CreateWorkItemHandler(
        IDocumentRepository<WorkItemDocument> workItems,
        IWorkItemNotificationPublisher notifications,
        IWorkItemAuditPublisher audit,
        IClock clock,
        ICurrentUser currentUser,
        IProjectPermissionChecker permissionChecker,
        IWorkItemTeamPolicy teamPolicy,
        IBoardPlacementPolicy boardPlacementPolicy,
        IDistributedLockProvider distributedLockProvider,
        IOptions<DistributedLockOptions> distributedLockOptions,
        IWorkItemSearchPublisher searchPublisher,
        IWorkItemRealtimePublisher realtimePublisher,
        IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
        IWorkItemActivityStore activityStore,
        WorkItemGraphService graph,
        WorkItemWipProjection? wipProjection,
        WorkItemRankService ranks,
        IWorkItemTypeSchemaPolicy typeSchemas,
        WorkItemCollaborationService? collaborationService,
        IWorkItemAutomationEventPublisher? automationEvents,
        IWorkItemAutomationChainContextAccessor? automationChain)
        : this(null!)
    {
        slice = new CreateWorkItemSlice(
            workItems,
            notifications,
            audit,
            clock,
            currentUser,
            permissionChecker,
            teamPolicy,
            boardPlacementPolicy,
            distributedLockProvider,
            distributedLockOptions,
            searchPublisher,
            realtimePublisher,
            cacheInvalidationPublisher,
            activityStore,
            graph,
            wipProjection,
            ranks,
            typeSchemas,
            collaborationService,
            automationEvents,
            automationChain);
    }

    public Task<WorkItemResponse> HandleAsync(
        CreateWorkItemRequest request,
        string correlationId,
        CancellationToken ct) =>
        HandleAsync(request, correlationId, ct, requestedId: null);

    internal Task<WorkItemResponse> HandleAsync(
        CreateWorkItemRequest request,
        string correlationId,
        CancellationToken ct,
        string? requestedId) =>
        slice?.HandleAsync(request, correlationId, ct, requestedId)
        ?? service.CreateAsync(request, correlationId, ct, requestedId);

    public Task<WorkItemResponse> CreateAsync(
        IntakeWorkItemCreation creation,
        CancellationToken ct) =>
        slice?.HandleAsync(creation, ct)
        ?? ((IIntakeWorkItemCreator)service).CreateAsync(creation, ct);

    internal Task<WorkItemResponse> HandleScopedAsync(
        CreateWorkItemRequest request,
        string correlationId,
        CreateWorkItemContext context,
        CancellationToken ct) =>
        slice?.HandleScopedAsync(request, correlationId, context, ct)
        ?? service.CreateAsync(request, correlationId, ct, context.RequestedId);
}
