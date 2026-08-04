using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostBulkMove(RouteGroupBuilder group){group.MapPost("/bulk/move", async (BulkMoveWorkItemsRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.BulkMoveAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemMove)
            .RequireRateLimiting("bulk");
}}
