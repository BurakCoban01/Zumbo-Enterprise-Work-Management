using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemReminderComposition
{
    internal static IServiceCollection AddWorkItemReminderHandlers(this IServiceCollection services)
    {
        services.AddScoped<SendDueDateRemindersHandler>(provider => new SendDueDateRemindersHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemNotificationPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>()));
        return services;
    }
}
