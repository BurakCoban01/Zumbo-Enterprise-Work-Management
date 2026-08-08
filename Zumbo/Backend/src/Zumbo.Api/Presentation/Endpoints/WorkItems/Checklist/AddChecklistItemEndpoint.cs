using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Checklist;

internal static class AddChecklistItemEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id}/checklist", async (
            string id,
            AddChecklistItemRequest request,
            AddChecklistItemHandler handler,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await handler.HandleAsync(new AddChecklistItemCommand(id, request), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
