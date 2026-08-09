using Zumbo.Api.Composition.Modules.WorkItems;

internal static partial class WorkItemEndpoints
{
    internal static IServiceCollection AddWorkItemsModule(
        this IServiceCollection services,
        IConfiguration? configuration = null) =>
        WorkItemModuleComposition.AddWorkItemsModule(services, configuration);
}
