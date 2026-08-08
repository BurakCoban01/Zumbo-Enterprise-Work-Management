using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemReportComposition
{
    internal static IServiceCollection AddWorkItemReportHandlers(this IServiceCollection services)
    {
        services.AddScoped<ProjectSummaryHandler>(provider => new ProjectSummaryHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));
        services.AddScoped<StatusDistributionHandler>(provider => new StatusDistributionHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));
        services.AddScoped<UserWorkloadHandler>(provider => new UserWorkloadHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>(),
            provider.GetRequiredService<IWorkItemActivityStore>()));
        services.AddScoped<DueDateRisksHandler>(provider => new DueDateRisksHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));
        services.AddScoped<FlowTimeHandler>(provider => new FlowTimeHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>(),
            provider.GetRequiredService<IWorkItemActivityStore>()));
        services.AddScoped<CompletionRateHandler>(provider => new CompletionRateHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));
        services.AddScoped<TeamPerformanceHandler>(provider => new TeamPerformanceHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemTeamPolicy>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>(),
            provider.GetRequiredService<IWorkItemActivityStore>()));
        return services;
    }
}
