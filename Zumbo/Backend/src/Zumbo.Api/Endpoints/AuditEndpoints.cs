using Zumbo.Modules.Audit;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class AuditEndpoints
{
    internal static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddOptions<AuditOptions>()
            .BindConfiguration("Audit")
            .Validate(options => options.RetentionDays is >= 30 and <= 3650
                && options.ExportMaxRecords is >= 1 and <= 100_000
                && options.RetentionBatchSize is >= 1 and <= 200
                && options.IntegrityMaxRecords is >= 1 and <= 1_000_000
                && (!options.HashChainEnabled || System.Text.Encoding.UTF8.GetByteCount(options.IntegrityKey) >= 32),
                "Audit retention, export, integrity or hash-chain configuration is invalid.")
            .ValidateOnStart();
        services.AddScoped<AuditAccessCheckerAdapter>();
        services.AddScoped<IAuditAccessChecker>(provider => provider.GetRequiredService<AuditAccessCheckerAdapter>());
        services.AddScoped<IAuditTenantResolver>(provider => provider.GetRequiredService<AuditAccessCheckerAdapter>());
        services.AddScoped<IAuditRequestContext, HttpAuditRequestContext>();
        services.AddScoped<AuditService>();
        services.AddScoped<WriteAuditLogHandler>();
        services.AddScoped<QueryAuditLogHandler>();
        return services;
    }

    internal static void MapAuditEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/audit").WithTags("Audit").RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.AuditRead);

        group.MapGet("/", async (
            string? actorUserId,
            string? action,
            string? entityType,
            string? entityId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize,
            string? cursor,
            string? organizationId,
            QueryAuditLogHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new AuditLogQuery(actorUserId, action, entityType, entityId, from, to, page ?? 1, pageSize ?? 50, cursor, organizationId),
                ct),
                http));

        group.MapGet("/export", async (
            string? actorUserId,
            string? action,
            string? entityType,
            string? entityId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? organizationId,
            AuditService service,
            CancellationToken ct) =>
        {
            var records = await service.ExportAsync(
                new AuditLogQuery(actorUserId, action, entityType, entityId, from, to, OrganizationId: organizationId),
                ct);
            var lines = string.Join('\n', records.Select(record =>
                System.Text.Json.JsonSerializer.Serialize(record))) + '\n';
            return Results.Text(lines, "application/x-ndjson", System.Text.Encoding.UTF8);
        }).RequireRateLimiting("report");

        group.MapPost("/retention/purge", async (
            string organizationId,
            AuditService service,
            IClock clock,
            CancellationToken ct) =>
            Results.Ok(await service.PurgeExpiredAsync(organizationId, clock.UtcNow, ct)))
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("bulk");

        group.MapGet("/integrity/{organizationId}", async (
            string organizationId,
            AuditService service,
            CancellationToken ct) =>
            Results.Ok(await service.VerifyIntegrityAsync(organizationId, ct)))
            .WithZumboPermission(PermissionCatalog.AuditReadAll, isGlobal: true)
            .RequireRateLimiting("report");

        group.MapGet("/entity/{entityType}/{entityId}", async (string entityType, string entityId, AuditService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListByEntityAsync(entityType, entityId, ct), http));

        group.MapGet("/user/{actorUserId}", async (string actorUserId, AuditService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListByUserAsync(actorUserId, ct), http));
    }
}
