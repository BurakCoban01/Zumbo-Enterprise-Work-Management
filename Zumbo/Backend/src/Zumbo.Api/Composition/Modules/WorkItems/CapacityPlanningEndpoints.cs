using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Scenarios;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Snapshots;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;

internal static class CapacityPlanningEndpoints
{
    internal static IServiceCollection AddCapacityPlanningModule(
        this IServiceCollection services)
    {
        services.AddScoped<ICapacityPlanningDirectory, CapacityPlanningDirectoryAdapter>();
        services.AddScoped<ICapacityPlanningAuditWriter, CapacityPlanningAuditWriterAdapter>();
        services.AddScoped<CapacityPlanAccessPolicy>();
        services.AddScoped<ArchiveCapacityPlanHandler>();
        services.AddScoped<GetCapacityPlanHandler>();
        services.AddScoped<ListCapacityPlansHandler>();
        services.AddScoped<ShareCapacityPlanHandler>();
        services.AddScoped<SaveCapacityPlanHandler>();
        services.AddScoped<GetCapacitySnapshotHandler>();
        services.AddScoped<PreviewScenarioHandler>();
        services.AddScoped<CapacityPlanningService>();
        return services;
    }
}
