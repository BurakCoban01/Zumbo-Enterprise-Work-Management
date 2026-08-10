using Zumbo.Api.Composition.Hosting.Registrars;

internal static partial class ApiHostRegistration
{
    private static (string Provider, bool IsWorkerRole) ConfigureRuntimeProviders(
        WebApplicationBuilder builder) =>
        ApiHostStorageRegistrar.ConfigureRuntimeProviders(builder);

    private static void ConfigureCoreServicesAndStorage(WebApplicationBuilder builder) =>
        ApiHostStorageRegistrar.ConfigureCoreServicesAndStorage(builder);
}
