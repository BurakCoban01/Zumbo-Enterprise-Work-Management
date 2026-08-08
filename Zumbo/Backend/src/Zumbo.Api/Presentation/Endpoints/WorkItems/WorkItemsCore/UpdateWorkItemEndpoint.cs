using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;

internal static class UpdateWorkItemEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id}", async (string id, UpdateWorkItemRequest request, UpdateWorkItemHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new UpdateWorkItemCommand(id, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
