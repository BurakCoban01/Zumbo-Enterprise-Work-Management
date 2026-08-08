using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemWorklogComposition
{
    internal static IServiceCollection AddWorkItemWorklogHandlers(this IServiceCollection services)
    {
        services.AddScoped<AddWorkLogHandler>(provider => new AddWorkLogHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<WorkItemCollaborationService>()));
        return services;
    }
}
