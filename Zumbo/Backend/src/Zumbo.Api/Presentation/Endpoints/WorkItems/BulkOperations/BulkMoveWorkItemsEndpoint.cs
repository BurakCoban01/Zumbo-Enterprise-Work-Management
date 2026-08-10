using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Move;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;

internal static class BulkMoveWorkItemsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk/move", async (BulkMoveWorkItemsRequest request, BulkMoveWorkItemsHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new BulkMoveWorkItemsCommand(request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemMove)
            .RequireRateLimiting("bulk");
    }
}
