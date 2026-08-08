using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemApprovalComposition
{
    internal static IServiceCollection AddWorkItemApprovalRequestHandler(this IServiceCollection services)
    {
        services.AddScoped<RequestApprovalHandler>(provider => new RequestApprovalHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemNotificationPublisher>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkflowPolicy>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>()));
        return services;
    }

    internal static IServiceCollection AddWorkItemApprovalDecisionHandler(this IServiceCollection services)
    {
        services.AddScoped<DecideApprovalHandler>(provider => new DecideApprovalHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemNotificationPublisher>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>()));
        return services;
    }
}
