using Zumbo.Api.Composition.Modules.WorkItems;
using Zumbo.Modules.WorkItems;

internal static class DashboardEndpoints
{
    internal static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        services.AddScoped<IDashboardViewerDirectory, DashboardViewerDirectoryAdapter>();
        services.AddScoped<IDashboardAuditWriter, DashboardAuditWriterAdapter>();
        services.AddScoped<DashboardService>();
        services.AddWorkItemDashboardRenderer();
        return services;
    }
}
