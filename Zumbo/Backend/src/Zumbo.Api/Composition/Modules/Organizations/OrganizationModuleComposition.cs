using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Organizations;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.Organizations;

internal static class OrganizationModuleComposition
{
    internal static IServiceCollection AddOrganizationServices(this IServiceCollection services)
    {
        services.AddOptions<OrganizationLifecycleOptions>()
            .BindConfiguration("OrganizationLifecycle")
            .Validate(
                options => options.ArchiveRetentionDays is >= 30 and <= 3650,
                "OrganizationLifecycle:ArchiveRetentionDays must be between 30 and 3650 days.")
            .ValidateOnStart();
        services.AddScoped<IOrganizationMemberDirectory, OrganizationMemberDirectoryAdapter>();
        services.AddScoped<IOrganizationAuditWriter, OrganizationAuditWriterAdapter>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<CreateOrganizationHandler>(provider => new CreateOrganizationHandler(
            provider.GetRequiredService<IDocumentRepository<OrganizationDocument>>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IOrganizationAuditWriter>()));
        services.AddScoped<ListOrganizationsHandler>(provider => new ListOrganizationsHandler(
            provider.GetRequiredService<IDocumentRepository<OrganizationDocument>>(),
            provider.GetRequiredService<ICurrentUser>()));
        return services;
    }
}
