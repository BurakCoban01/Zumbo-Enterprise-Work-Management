using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;

internal static class RestoreWorkItemEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id}/restore", async (string id, RestoreWorkItemHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new RestoreWorkItemCommand(id, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemDelete);
    }
}
