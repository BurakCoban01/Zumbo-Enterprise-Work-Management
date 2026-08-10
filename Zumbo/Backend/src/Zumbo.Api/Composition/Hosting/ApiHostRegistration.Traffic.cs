using Zumbo.Api.Composition.Hosting.Registrars;

internal static partial class ApiHostRegistration
{
    private static void ConfigureRateLimiting(WebApplicationBuilder builder) =>
        ApiHostTrafficRegistrar.ConfigureRateLimiting(builder);
}
