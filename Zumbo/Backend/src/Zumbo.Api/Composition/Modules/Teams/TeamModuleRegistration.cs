using Zumbo.Api.Composition.Modules.Teams;

internal static class TeamModuleRegistration
{
    internal static IServiceCollection AddTeamsModule(this IServiceCollection services) =>
        services.AddTeamServices();
}
