using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;

internal static class MoveWorkItemEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id}/status", async (string id, MoveWorkItemRequest request, MoveWorkItemHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new MoveWorkItemCommand(id, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemMove);
    }
}
