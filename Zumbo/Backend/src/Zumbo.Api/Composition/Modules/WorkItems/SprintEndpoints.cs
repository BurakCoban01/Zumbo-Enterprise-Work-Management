using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Sprints;
using Zumbo.SharedKernel;

internal static class SprintEndpoints
{
    internal static IServiceCollection AddSprintsModule(this IServiceCollection services)
    {
        services.AddOptions<SprintOptions>()
            .BindConfiguration("Sprint")
            .Validate(
                options => options.BatchSize is >= 1 and <= 200
                    && options.MaxBatchesPerOperation is >= 1 and <= 10_000,
                "Sprint batch settings are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<IWorkItemSprintPolicy, WorkItemSprintPolicyAdapter>();
        services.AddScoped<SprintService>();
        services.AddScoped<GetSprintHandler>(provider => new GetSprintHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<ListSprintsHandler>(provider => new ListSprintsHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<ListSprintBacklogHandler>(provider => new ListSprintBacklogHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetSprintBurndownHandler>(provider => new GetSprintBurndownHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<SprintScopeSnapshotDocument>>(),
            provider.GetRequiredService<IDocumentRepository<SprintCompletionSnapshotDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IOptions<SprintOptions>>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));
        services.AddScoped<GetSprintVelocityHandler>(provider => new GetSprintVelocityHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));
        services.AddScoped<CreateSprintHandler>(provider => new CreateSprintHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>()));
        services.AddScoped<StartSprintHandler>(provider => new StartSprintHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<SprintScopeSnapshotDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IOptions<SprintOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<CompleteSprintHandler>(provider => new CompleteSprintHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<SprintScopeSnapshotDocument>>(),
            provider.GetRequiredService<IDocumentRepository<SprintCompletionSnapshotDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IOptions<SprintOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<PlanSprintWorkItemHandler>(provider => new PlanSprintWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<UnplanSprintWorkItemHandler>(provider => new UnplanSprintWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        return services;
    }

}
