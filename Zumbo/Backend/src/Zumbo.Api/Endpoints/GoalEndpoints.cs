using Microsoft.AspNetCore.Mvc;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;

using static ApiEndpointResults;

internal static class GoalEndpoints
{
    internal static IServiceCollection AddGoalModule(this IServiceCollection services)
    {
        services.AddScoped<IGoalDirectory, GoalDirectoryAdapter>();
        services.AddScoped<IGoalAuditWriter, GoalAuditWriterAdapter>();
        services.AddScoped<GoalService>();
        return services;
    }

    internal static void MapGoalEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/goals")
            .WithTags("Goals")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.ProjectView);

        group.MapGet("", async (
            bool? includeArchived,
            int? page,
            int? pageSize,
            [FromServices] GoalService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListAsync(
                includeArchived ?? false,
                page ?? 1,
                pageSize ?? 50,
                ct), http));

        group.MapGet("/{goalId}", async (
            string goalId,
            bool? includeArchived,
            [FromServices] GoalService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(goalId, includeArchived ?? false, ct), http));

        group.MapGet("/{goalId}/rollup", async (
            string goalId,
            [FromServices] GoalService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetRollupAsync(goalId, ct), http));

        group.MapPost("", async (
            SaveGoalRequest request,
            [FromServices] GoalService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveAsync(null, request, CorrelationId(http), ct), http));

        group.MapPut("/{goalId}", async (
            string goalId,
            SaveGoalRequest request,
            [FromServices] GoalService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveAsync(goalId, request, CorrelationId(http), ct), http));

        group.MapPost("/{goalId}/key-results", async (
            string goalId,
            SaveKeyResultRequest request,
            [FromServices] GoalService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveKeyResultAsync(
                goalId, null, request, CorrelationId(http), ct), http));

        group.MapPut("/{goalId}/key-results/{keyResultId}", async (
            string goalId,
            string keyResultId,
            SaveKeyResultRequest request,
            [FromServices] GoalService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveKeyResultAsync(
                goalId, keyResultId, request, CorrelationId(http), ct), http));

        group.MapPost("/{goalId}/key-results/{keyResultId}/progress-updates", async (
            string goalId,
            string keyResultId,
            AddKeyResultProgressRequest request,
            [FromServices] GoalService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.AddKeyResultProgressAsync(
                goalId, keyResultId, request, CorrelationId(http), ct), http));

        group.MapPost("/{goalId}/status-updates", async (
            string goalId,
            AddGoalStatusUpdateRequest request,
            [FromServices] GoalService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.AddStatusUpdateAsync(
                goalId, request, CorrelationId(http), ct), http));

        group.MapDelete("/{goalId}", async (
            string goalId,
            [FromServices] GoalService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveAsync(goalId, CorrelationId(http), ct);
            return Ok(new { archived = true }, http);
        });
    }
}
