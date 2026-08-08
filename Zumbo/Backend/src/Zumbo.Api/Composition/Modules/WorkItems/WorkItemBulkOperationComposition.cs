using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Archive;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Assign;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Move;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemBulkOperationComposition
{
    internal static IServiceCollection AddWorkItemBulkOperations(this IServiceCollection services)
    {
        services.AddOptions<WorkItemBulkJobOptions>()
            .BindConfiguration("WorkItemBulkJobs")
            .Validate(
                options => options.BatchSize is >= 1 and <= 200
                    && options.MaxInputItems is >= 1 and <= 10_000
                    && options.MaxInputBytes is >= 1_024 and <= 50 * 1024 * 1024
                    && options.MaxExportItems is >= 1 and <= 100_000
                    && options.MaxArtifactBytes is >= 1_024 and <= 100 * 1024 * 1024
                    && options.ArtifactRetentionDays is >= 1 and <= 90,
                "Work-item bulk job limits are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<IWorkItemBulkArtifactStorage, WorkItemBulkArtifactStorageAdapter>();
        services.AddScoped<WorkItemBulkJobService>();
        services.AddScoped<WorkItemBulkJobProcessor>(provider => new WorkItemBulkJobProcessor(
            provider.GetRequiredService<IDocumentRepository<WorkItemBulkJobDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemBulkJobItemDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IBoardPlacementPolicy>(),
            provider.GetRequiredService<IWorkItemTeamPolicy>(),
            provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),
            provider.GetRequiredService<IWorkItemBulkJobEventPublisher>(),
            provider.GetRequiredService<IWorkItemBulkArtifactStorage>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IOptions<WorkItemBulkJobOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<CreateWorkItemHandler>(),
            provider.GetRequiredService<MoveWorkItemHandler>(),
            provider.GetRequiredService<AssignWorkItemHandler>(),
            provider.GetRequiredService<ArchiveWorkItemHandler>()));
        services.AddScoped<BulkMoveWorkItemsHandler>();
        services.AddScoped<BulkAssignWorkItemsHandler>();
        services.AddScoped<BulkArchiveWorkItemsHandler>();
        return services;
    }
}
