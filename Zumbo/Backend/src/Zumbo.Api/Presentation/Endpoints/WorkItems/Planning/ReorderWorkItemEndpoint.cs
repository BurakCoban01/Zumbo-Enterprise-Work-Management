using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Planning;

internal static class ReorderWorkItemEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id}/rank", async (string id, ReorderWorkItemRequest request, ReorderWorkItemHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new ReorderWorkItemCommand(id, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemMove);
    }
}
