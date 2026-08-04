using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostSearchRebuild(RouteGroupBuilder group){group.MapPost("/search/rebuild", async (
            SearchMaintenanceService service,
            IWorkItemOperationsAuditWriter audit,
            HttpContext http,
            CancellationToken ct) =>
        {
            var result = await service.RebuildAsync(ct);
            await audit.WriteAsync(
                "SearchIndexRebuilt",
                "Operations",
                "work-item-search",
                null,
                $"{result.Indexed}:{result.Removed}:{result.AliasChanged}",
                CorrelationId(http),
                ct);
            return Results.Ok(result);
        })
            .WithZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)
            .RequireRateLimiting("bulk");
}}
