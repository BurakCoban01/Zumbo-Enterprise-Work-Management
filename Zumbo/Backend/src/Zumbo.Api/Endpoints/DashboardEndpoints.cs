using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

internal static class DashboardEndpoints
{
    internal static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        services.AddScoped<IDashboardViewerDirectory, DashboardViewerDirectoryAdapter>();
        services.AddScoped<IDashboardAuditWriter, DashboardAuditWriterAdapter>();
        services.AddScoped<DashboardService>();
        services.AddScoped<DashboardRenderer>();
        return services;
    }

    internal static void MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/dashboards")
            .WithTags("Dashboards")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.WorkItemView);
        group.AddEndpointFilter<WorkItemTransactionFilter>();

        group.MapGet("", async (
            bool? includeArchived,
            int? page,
            int? pageSize,
            [FromServices] DashboardService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListAsync(
                includeArchived ?? false,
                page ?? 1,
                pageSize ?? 50,
                ct), http));

        group.MapGet("/{dashboardId}", async (
            string dashboardId,
            bool? includeArchived,
            [FromServices] DashboardService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(dashboardId, includeArchived ?? false, ct), http));

        group.MapPost("", async (
            SaveDashboardRequest request,
            [FromServices] DashboardService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveAsync(null, request, CorrelationId(http), ct), http));

        group.MapPut("/{dashboardId}", async (
            string dashboardId,
            SaveDashboardRequest request,
            [FromServices] DashboardService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveAsync(
                dashboardId,
                request,
                CorrelationId(http),
                ct), http));

        group.MapPut("/{dashboardId}/sharing", async (
            string dashboardId,
            ShareDashboardRequest request,
            [FromServices] DashboardService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ShareAsync(
                dashboardId,
                request,
                CorrelationId(http),
                ct), http));

        group.MapDelete("/{dashboardId}", async (
            string dashboardId,
            [FromServices] DashboardService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveAsync(dashboardId, CorrelationId(http), ct);
            return Ok(new { archived = true }, http);
        });

        group.MapGet("/{dashboardId}/export", async (
            string dashboardId,
            [FromServices] DashboardService service,
            CancellationToken ct) =>
        {
            var dashboard = await service.GetAsync(dashboardId, includeArchived: false, ct);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                dashboard,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            return Results.File(
                bytes,
                "application/json",
                $"zumbo-dashboard-{dashboard.Id}.json");
        }).RequireRateLimiting("report");

        group.MapGet("/{dashboardId}/render", async (
            string dashboardId,
            [FromServices] DashboardRenderer renderer,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await renderer.RenderAsync(dashboardId, ct), http))
            .RequireRateLimiting("report");
    }
}
