using Zumbo.Api.Composition.Hosting.Registrars;

internal static partial class ApiHostRegistration
{
    private static void ConfigureBackgroundJobsAndHealth(
        WebApplicationBuilder builder,
        string provider,
        bool isWorkerRole) =>
        ApiHostOperationsRegistrar.ConfigureBackgroundJobsAndHealth(builder, provider, isWorkerRole);
}
