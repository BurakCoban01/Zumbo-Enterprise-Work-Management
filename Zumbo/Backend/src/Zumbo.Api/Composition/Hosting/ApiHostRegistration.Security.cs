internal static partial class ApiHostRegistration
{
    private static void ConfigureAuthentication(WebApplicationBuilder builder) =>
        ApiHostSecurityRegistrar.ConfigureAuthentication(builder);

    private static void ConfigureDataProtectionAndRealtime(WebApplicationBuilder builder) =>
        ApiHostSecurityRegistrar.ConfigureDataProtectionAndRealtime(builder);
}
