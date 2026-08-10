using Zumbo.Api.Composition.Modules.Audit;
using Zumbo.Modules.Audit;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class AuditEndpoints
{
    internal static IServiceCollection AddAuditModule(this IServiceCollection services) =>
        services.AddAuditServices();

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
            HttpContext http,
            CancellationToken ct) =>
        {
            var records = await service.ExportAsync(
                new AuditLogQuery(actorUserId, action, entityType, entityId, from, to, OrganizationId: organizationId),
                ct);
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = "application/x-ndjson";
            http.Response.Headers.ContentDisposition =
                "attachment; filename=zumbo-audit-export.ndjson";
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers["X-Zumbo-Export-Format"] = "audit-ndjson-v1";
            http.Response.Headers["X-Zumbo-Export-Records"] =
                records.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await AuditService.WriteNdjsonAsync(records, http.Response.Body, ct);
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
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.VerifyIntegrityAsync(organizationId, ct), http))
            .WithZumboPermission(PermissionCatalog.AuditReadAll, isGlobal: true)
            .RequireRateLimiting("report");

        group.MapGet("/entity/{entityType}/{entityId}", async (string entityType, string entityId, AuditService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListByEntityAsync(entityType, entityId, ct), http));

        group.MapGet("/user/{actorUserId}", async (string actorUserId, AuditService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListByUserAsync(actorUserId, ct), http));
    }
}
