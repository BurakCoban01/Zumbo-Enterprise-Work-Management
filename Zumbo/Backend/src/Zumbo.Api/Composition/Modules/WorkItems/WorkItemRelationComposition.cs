using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemRelationComposition
{
    internal static IServiceCollection AddWorkItemRelationHandlers(this IServiceCollection services)
    {
        services.AddScoped<SetParentHandler>(provider => new SetParentHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetRequiredService<WorkItemGraphService>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>()));
        services.AddScoped<LinkWorkItemHandler>(provider => new LinkWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetRequiredService<WorkItemGraphService>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>()));
        services.AddScoped<UnlinkWorkItemHandler>(provider => new UnlinkWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetRequiredService<WorkItemGraphService>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>()));
        return services;
    }
}
