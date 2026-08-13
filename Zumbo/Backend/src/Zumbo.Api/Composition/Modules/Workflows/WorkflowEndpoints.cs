using Zumbo.Api.Composition.Modules.Workflows;

internal static class WorkflowEndpoints
{
    internal static IServiceCollection AddWorkflowsModule(
        this IServiceCollection services,
        IConfiguration? configuration = null) =>
        services.AddWorkflowServices(configuration);

}
