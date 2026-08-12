using Zumbo.Api.Composition.Modules.Boards;

internal static class BoardModuleRegistration
{
    internal static IServiceCollection AddBoardsModule(this IServiceCollection services) =>
        services.AddBoardServices();
}
