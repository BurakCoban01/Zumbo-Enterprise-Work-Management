using Zumbo.Api.Composition.Modules.Projects;

internal static class ProjectModuleRegistration
{
    internal static IServiceCollection AddProjectsModule(this IServiceCollection services) =>
        services.AddProjectServices();
}
