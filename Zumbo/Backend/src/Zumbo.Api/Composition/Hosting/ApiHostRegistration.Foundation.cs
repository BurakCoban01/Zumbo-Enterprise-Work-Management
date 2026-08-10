using Zumbo.Api.Composition.Hosting.Registrars;

internal static partial class ApiHostRegistration
{
    internal static WebApplicationBuilder AddZumboHost(this WebApplicationBuilder builder)
    {
        ConfigureHostFoundation(builder);
        ConfigureRateLimiting(builder);
        ConfigureAuthentication(builder);
        ConfigureDataProtectionAndRealtime(builder);
        ConfigureCoreServicesAndStorage(builder);
        var (provider, isWorkerRole) = ConfigureRuntimeProviders(builder);
        ConfigureBackgroundJobsAndHealth(builder, provider, isWorkerRole);
        return builder;
    }

    private static void ConfigureHostFoundation(WebApplicationBuilder builder) =>
        ApiHostFoundationRegistrar.ConfigureHostFoundation(builder);

    private static void ValidateRegistrationProvisioning(WebApplicationBuilder builder) =>
        ApiHostFoundationRegistrar.ValidateRegistrationProvisioning(builder);
}
