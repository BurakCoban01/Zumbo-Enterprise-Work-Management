using Zumbo.Api.Composition.Modules.Notifications;

internal static class NotificationModuleRegistration
{
    internal static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddNotificationServices(configuration);
}
