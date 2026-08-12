using Zumbo.Api.Composition.Modules.Audit;

internal static class AuditModuleRegistration
{
    internal static IServiceCollection AddAuditModule(this IServiceCollection services) =>
        services.AddAuditServices();
}
