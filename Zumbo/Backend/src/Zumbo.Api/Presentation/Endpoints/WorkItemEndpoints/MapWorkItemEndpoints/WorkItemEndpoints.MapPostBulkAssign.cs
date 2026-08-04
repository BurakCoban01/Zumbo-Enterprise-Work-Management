using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostBulkAssign(RouteGroupBuilder group){group.MapPost("/bulk/assign", async (BulkAssignWorkItemsRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.BulkAssignAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemAssign)
            .RequireRateLimiting("bulk");
}}
