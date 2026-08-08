using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Labels;

internal static class AddLabelEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id}/labels", async (
            string id,
            AddLabelRequest request,
            AddLabelHandler handler,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await handler.HandleAsync(new AddLabelCommand(id, request), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
