using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemPlanningComposition
{
    internal static IServiceCollection AddWorkItemPlanningHandlers(this IServiceCollection services)
    {
        services.AddScoped<SetPlanningHandler>(provider => new SetPlanningHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetService<IWorkItemSprintPolicy>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<WorkItemCollaborationService>()));
        services.AddScoped<MoveWorkItemHandler>(provider => new MoveWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkflowPolicy>(),
            provider.GetRequiredService<IBoardPlacementPolicy>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetRequiredService<WorkItemGraphService>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemWipProjection>(),
            provider.GetRequiredService<WorkItemRankService>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<IWorkItemAutomationEventPublisher>(),
            provider.GetService<IWorkItemAutomationChainContextAccessor>()));
        services.AddScoped<ReorderWorkItemHandler>(provider => new ReorderWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetRequiredService<WorkItemRankService>(),
            provider.GetService<WorkItemCollaborationService>()));
        return services;
    }
}
