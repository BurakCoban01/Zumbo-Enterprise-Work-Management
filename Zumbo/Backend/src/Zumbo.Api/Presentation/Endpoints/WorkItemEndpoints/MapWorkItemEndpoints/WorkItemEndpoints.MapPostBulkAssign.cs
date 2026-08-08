using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Assign;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostBulkAssign(RouteGroupBuilder group){group.MapPost("/bulk/assign", async (BulkAssignWorkItemsRequest request, BulkAssignWorkItemsHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new BulkAssignWorkItemsCommand(request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemAssign)
            .RequireRateLimiting("bulk");
}}
