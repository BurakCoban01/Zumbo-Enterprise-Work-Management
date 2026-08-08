using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Teams;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.Teams;

internal static class TeamModuleComposition
{
    internal static IServiceCollection AddTeamServices(this IServiceCollection services)
    {
        services.AddScoped<ITeamUserDirectory, TeamUserDirectoryAdapter>();
        services.AddScoped<ITeamOrganizationDirectory, TeamOrganizationDirectoryAdapter>();
        services.AddScoped<ITeamAuditWriter, TeamAuditWriterAdapter>();
        services.AddScoped<DurableTeamInvitationPublisher>();
        services.AddScoped<ITeamInvitationNotifier>(provider =>
            provider.GetRequiredService<DurableTeamInvitationPublisher>());
        services.AddScoped<IDurableEventHandler, TeamInvitationNotificationHandler>();
        services.AddScoped<TeamTransactionFilter>();
        services.AddScoped<TeamService>();
        services.AddScoped<CreateTeamHandler>(provider => new CreateTeamHandler(
            provider.GetRequiredService<IDocumentRepository<TeamDocument>>(),
            provider.GetRequiredService<ITeamUserDirectory>(),
            provider.GetRequiredService<ITeamOrganizationDirectory>(),
            provider.GetRequiredService<ITeamAuditWriter>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<ListTeamsHandler>(provider => new ListTeamsHandler(
            provider.GetRequiredService<IDocumentRepository<TeamDocument>>(),
            provider.GetRequiredService<ITeamOrganizationDirectory>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>()));
        return services;
    }
}
