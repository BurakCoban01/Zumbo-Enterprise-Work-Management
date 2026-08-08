using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Labels;

internal static class RemoveLabelEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id}/labels/{label}", async (
            string id,
            string label,
            RemoveLabelHandler handler,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await handler.HandleAsync(new RemoveLabelCommand(id, label), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
