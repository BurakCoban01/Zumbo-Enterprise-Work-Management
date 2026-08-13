using Zumbo.Api.Composition.Modules.Organizations;

internal static class OrganizationModuleRegistration
{
    internal static IServiceCollection AddOrganizationsModule(this IServiceCollection services) =>
        services.AddOrganizationServices();
}
