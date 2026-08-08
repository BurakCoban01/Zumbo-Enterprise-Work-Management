using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemAssignmentComposition
{
    internal static IServiceCollection AddWorkItemAssignmentHandlers(this IServiceCollection services)
    {
        services.AddScoped<ClearAssigneeHandler>(provider => new ClearAssigneeHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<IWorkItemAutomationEventPublisher>(),
            provider.GetService<IWorkItemAutomationChainContextAccessor>()));
        services.AddScoped<AssignWorkItemHandler>(provider => new AssignWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetRequiredService<IWorkItemTeamPolicy>(),
            provider.GetRequiredService<IWorkItemNotificationPublisher>()));
        services.AddScoped<SetWorkItemTeamHandler>(provider => new SetWorkItemTeamHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetRequiredService<IWorkItemTeamPolicy>()));
        return services;
    }
}
