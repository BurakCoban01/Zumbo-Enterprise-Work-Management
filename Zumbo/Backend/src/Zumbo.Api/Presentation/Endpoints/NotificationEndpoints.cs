using Zumbo.Modules.Notifications;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class NotificationEndpoints
{
    internal static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<EmailNotificationOptions>()
            .BindConfiguration("Notifications:Email")
            .Validate(options => !options.Enabled
                || (!string.IsNullOrWhiteSpace(options.Host)
                    && options.Port is >= 1 and <= 65535
                    && options.MaxAttempts is >= 1 and <= 20
                    && options.BaseRetrySeconds is >= 1 and <= 3600
                    && options.MaximumRetrySeconds >= options.BaseRetrySeconds
                    && options.MaximumRetrySeconds <= 86400
                    && options.RetryJitterRatio is >= 0 and <= 1
                    && options.LeaseSeconds is >= 5 and <= 900
                    && options.DispatchBatchSize is >= 1 and <= 100
                    && options.DispatcherIntervalSeconds is >= 1 and <= 3600),
                "Notification email delivery configuration is invalid.")
            .ValidateOnStart();
        services.AddScoped<INotificationUserDirectory, NotificationUserDirectoryAdapter>();
        services.AddScoped<INotificationAuditWriter, NotificationAuditWriterAdapter>();
        services.AddScoped<IEmailNotificationSender, SmtpEmailNotificationSender>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ListNotificationsHandler>(provider => new ListNotificationsHandler(
            provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<MarkNotificationAsReadHandler>(provider => new MarkNotificationAsReadHandler(
            provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),
            provider.GetRequiredService<ICurrentUser>()));
        if (configuration.GetValue("BackgroundJobs:Enabled", true))
        {
            services.AddHostedService<NotificationEmailDispatcherHostedService>();
        }

        return services;
    }

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

        group.MapGet("/preferences/me", async (NotificationService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.GetPreferencesAsync(ct), http));

        group.MapPut("/preferences/me", async (
            UpdateNotificationPreferencesRequest request,
            NotificationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.UpdatePreferencesAsync(request, ct), http))
            .WithZumboPermission(PermissionCatalog.NotificationManage);

        group.MapPatch("/{notificationId}/read", async (string notificationId, MarkNotificationAsReadHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new MarkNotificationAsReadCommand(notificationId), ct), http))
            .WithZumboPermission(PermissionCatalog.NotificationManage);

        group.MapGet("/delivery/status", async (
            string organizationId,
            NotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetDeliveryMetricsAsync(organizationId, ct)))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("report");

        group.MapGet("/delivery/dead-letters", async (
            string organizationId,
            int? pageSize,
            NotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.ListDeadLettersAsync(
                organizationId,
                Math.Clamp(pageSize ?? 20, 1, 50),
                ct)))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("report");

        group.MapPost("/delivery/{notificationId}/replay", async (
            string notificationId,
            string organizationId,
            NotificationService service,
            INotificationAuditWriter audit,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!await service.ReplayDeadLetterAsync(organizationId, notificationId, ct))
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
