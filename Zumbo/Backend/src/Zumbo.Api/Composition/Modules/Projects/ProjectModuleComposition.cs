using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.Projects;

internal static class ProjectModuleComposition
{
    internal static IServiceCollection AddProjectServices(this IServiceCollection services)
    {
        services.AddOptions<ProjectLifecycleOptions>()
            .BindConfiguration("ProjectLifecycle")
            .Validate(
                options => options.ArchiveRetentionDays is >= 30 and <= 3650,
                "ProjectLifecycle:ArchiveRetentionDays must be between 30 and 3650 days.")
            .ValidateOnStart();
        services.AddScoped<IProjectResourcePolicy, ProjectResourcePolicyAdapter>();
        services.AddScoped<IProjectMemberDirectory, ProjectMemberDirectoryAdapter>();
        services.AddScoped<IProjectOrganizationDirectory, ProjectOrganizationDirectoryAdapter>();
        services.AddScoped<IProjectTeamDirectory, ProjectTeamDirectoryAdapter>();
        services.AddScoped<IProjectTeamUsageChecker, ProjectTeamUsageCheckerAdapter>();
        services.AddScoped<IProjectAuditWriter, ProjectAuditWriterAdapter>();
        services.AddScoped<ProjectService>();
        services.AddScoped<CreateProjectHandler>(provider => new CreateProjectHandler(
            provider.GetRequiredService<IDocumentRepository<ProjectDocument>>(),
            provider.GetRequiredService<IProjectMemberDirectory>(),
            provider.GetRequiredService<IProjectOrganizationDirectory>(),
            provider.GetRequiredService<IProjectAuditWriter>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<ListProjectsHandler>(provider => new ListProjectsHandler(
            provider.GetRequiredService<IDocumentRepository<ProjectDocument>>(),
            provider.GetRequiredService<IProjectOrganizationDirectory>(),
            provider.GetRequiredService<ICurrentUser>()));
        return services;
    }
}
