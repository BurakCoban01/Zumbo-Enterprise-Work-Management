using Microsoft.AspNetCore.Mvc;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

internal static class CapacityPlanningEndpoints
{
    internal static IServiceCollection AddCapacityPlanningModule(
        this IServiceCollection services)
    {
        services.AddScoped<ICapacityPlanningDirectory, CapacityPlanningDirectoryAdapter>();
        services.AddScoped<ICapacityPlanningAuditWriter, CapacityPlanningAuditWriterAdapter>();
        services.AddScoped<CapacityPlanningService>();
        return services;
    }

    internal static void MapCapacityPlanningEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/capacity-plans")
            .WithTags("CapacityPlanning")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.WorkItemView);
        group.AddEndpointFilter<WorkItemTransactionFilter>();

        group.MapGet("", async (
            bool? includeArchived,
            int? page,
            int? pageSize,
            [FromServices] CapacityPlanningService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListAsync(
                includeArchived ?? false,
                page ?? 1,
                pageSize ?? 50,
                ct), http));

        group.MapGet("/{planId}", async (
            string planId,
            bool? includeArchived,
            [FromServices] CapacityPlanningService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(planId, includeArchived ?? false, ct), http));

        group.MapPost("", async (
            SaveCapacityPlanRequest request,
            [FromServices] CapacityPlanningService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveAsync(null, request, CorrelationId(http), ct), http));

        group.MapPut("/{planId}", async (
            string planId,
            SaveCapacityPlanRequest request,
            [FromServices] CapacityPlanningService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveAsync(
                planId,
                request,
                CorrelationId(http),
                ct), http));

        group.MapPut("/{planId}/sharing", async (
            string planId,
            ShareCapacityPlanRequest request,
            [FromServices] CapacityPlanningService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ShareAsync(
                planId,
                request,
                CorrelationId(http),
                ct), http));

        group.MapDelete("/{planId}", async (
            string planId,
            [FromServices] CapacityPlanningService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveAsync(planId, CorrelationId(http), ct);
            return Ok(new { archived = true }, http);
        });

        group.MapGet("/{planId}/snapshot", async (
            string planId,
            [FromServices] CapacityPlanningService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetSnapshotAsync(planId, ct), http))
            .RequireRateLimiting("report");

        group.MapPost("/{planId}/scenarios", async (
            string planId,
            CapacityScenarioRequest request,
            [FromServices] CapacityPlanningService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.PreviewScenarioAsync(planId, request, ct), http))
            .RequireRateLimiting("report");
    }
}
