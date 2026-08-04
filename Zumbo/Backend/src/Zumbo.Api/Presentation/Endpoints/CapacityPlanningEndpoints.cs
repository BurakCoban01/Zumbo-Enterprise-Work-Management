using Microsoft.AspNetCore.Mvc;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Scenarios;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Snapshots;
using Zumbo.Modules.WorkItems.Application.Policies.CapacityPlanning;

using static ApiEndpointResults;

internal static class CapacityPlanningEndpoints
{
    internal static IServiceCollection AddCapacityPlanningModule(
        this IServiceCollection services)
    {
        services.AddScoped<ICapacityPlanningDirectory, CapacityPlanningDirectoryAdapter>();
        services.AddScoped<ICapacityPlanningAuditWriter, CapacityPlanningAuditWriterAdapter>();
        services.AddScoped<CapacityPlanAccessPolicy>();
        services.AddScoped<ArchiveCapacityPlanHandler>();
        services.AddScoped<GetCapacityPlanHandler>();
        services.AddScoped<ListCapacityPlansHandler>();
        services.AddScoped<ShareCapacityPlanHandler>();
        services.AddScoped<SaveCapacityPlanHandler>();
        services.AddScoped<GetCapacitySnapshotHandler>();
        services.AddScoped<PreviewScenarioHandler>();
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
            [FromServices] ListCapacityPlansHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ListCapacityPlansQuery(
                    includeArchived ?? false,
                    page ?? 1,
                    pageSize ?? 50),
                ct), http));

        group.MapGet("/{planId}", async (
            string planId,
            bool? includeArchived,
            [FromServices] GetCapacityPlanHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new GetCapacityPlanQuery(planId, includeArchived ?? false),
                ct), http));

        group.MapPost("", async (
            SaveCapacityPlanRequest request,
            [FromServices] SaveCapacityPlanHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SaveCapacityPlanCommand(null, request, CorrelationId(http)),
                ct), http));

        group.MapPut("/{planId}", async (
            string planId,
            SaveCapacityPlanRequest request,
            [FromServices] SaveCapacityPlanHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SaveCapacityPlanCommand(
                    planId,
                    request,
                    CorrelationId(http)),
                ct), http));

        group.MapPut("/{planId}/sharing", async (
            string planId,
            ShareCapacityPlanRequest request,
            [FromServices] ShareCapacityPlanHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ShareCapacityPlanCommand(
                    planId,
                    request,
                    CorrelationId(http)),
                ct), http));

        group.MapDelete("/{planId}", async (
            string planId,
            [FromServices] ArchiveCapacityPlanHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new ArchiveCapacityPlanCommand(planId, CorrelationId(http)),
                ct);
            return Ok(new { archived = true }, http);
        });

        group.MapGet("/{planId}/snapshot", async (
            string planId,
            [FromServices] GetCapacitySnapshotHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new GetCapacitySnapshotQuery(planId),
                ct), http))
            .RequireRateLimiting("report");

        group.MapPost("/{planId}/scenarios", async (
            string planId,
            CapacityScenarioRequest request,
            [FromServices] PreviewScenarioHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new PreviewScenarioQuery(planId, request),
                ct), http))
            .RequireRateLimiting("report");
    }
}
