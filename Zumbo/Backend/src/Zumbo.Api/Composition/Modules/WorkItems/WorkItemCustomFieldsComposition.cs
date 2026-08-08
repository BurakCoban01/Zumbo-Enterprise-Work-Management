using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemCustomFieldsComposition
{
    internal static IServiceCollection AddWorkItemCustomFieldsHandlers(this IServiceCollection services)
    {
        services.AddScoped<SetCustomFieldsHandler>(provider => new SetCustomFieldsHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),
            provider.GetRequiredService<IWorkItemSearchPublisher>(),
            provider.GetRequiredService<IWorkItemRealtimePublisher>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<IWorkItemAutomationEventPublisher>(),
            provider.GetService<IWorkItemAutomationChainContextAccessor>()));
        return services;
    }
}
