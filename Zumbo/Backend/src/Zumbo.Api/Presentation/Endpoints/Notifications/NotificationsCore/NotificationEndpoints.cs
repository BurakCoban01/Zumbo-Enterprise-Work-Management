using Zumbo.Api.Composition.Modules.Notifications;
using Zumbo.Modules.Notifications;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class NotificationEndpoints
{
    internal static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddNotificationServices(configuration);

    internal static void MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.NotificationView);

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            bool? unreadOnly,
            ListNotificationsHandler handler,
            ICurrentUser currentUser,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ListNotificationsQuery(
                    currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required."),
                    page ?? 1,
                    pageSize ?? 50,
                    unreadOnly ?? false),
                ct), http));

        group.MapGet("/{userId}", async (
            string userId,
            int? page,
            int? pageSize,
            bool? unreadOnly,
            ListNotificationsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new ListNotificationsQuery(userId, page ?? 1, pageSize ?? 50, unreadOnly ?? false), ct), http));

        group.MapGet("/preferences/me", async (
            GetNotificationPreferencesHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetNotificationPreferencesQuery(), ct), http));

        group.MapPut("/preferences/me", async (
            UpdateNotificationPreferencesRequest request,
            UpdateNotificationPreferencesHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new UpdateNotificationPreferencesCommand(request), ct), http))
            .WithZumboPermission(PermissionCatalog.NotificationManage);

        group.MapPatch("/{notificationId}/read", async (string notificationId, MarkNotificationAsReadHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new MarkNotificationAsReadCommand(notificationId), ct), http))
            .WithZumboPermission(PermissionCatalog.NotificationManage);

        group.MapGet("/delivery/status", async (
            string organizationId,
            GetNotificationDeliveryMetricsHandler handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(
                new GetNotificationDeliveryMetricsQuery(organizationId), ct)))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("report");

        group.MapGet("/delivery/dead-letters", async (
            string organizationId,
            int? pageSize,
            ListNotificationDeadLettersHandler handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(
                new ListNotificationDeadLettersQuery(
                    organizationId,
                    Math.Clamp(pageSize ?? 20, 1, 50)),
                ct)))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("report");

        group.MapPost("/delivery/{notificationId}/replay", async (
            string notificationId,
            string organizationId,
            ReplayNotificationDeadLetterHandler handler,
            INotificationAuditWriter audit,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!await handler.HandleAsync(
                    new ReplayNotificationDeadLetterCommand(organizationId, notificationId),
                    ct))
            {
                return Results.NotFound();
            }

            await audit.WriteAsync(
                "NotificationDeliveryReplayed",
                notificationId,
                "DeadLetter",
                "Pending",
                CorrelationId(http),
                ct);
            return Results.Ok();
        })
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("bulk");
    }
}
