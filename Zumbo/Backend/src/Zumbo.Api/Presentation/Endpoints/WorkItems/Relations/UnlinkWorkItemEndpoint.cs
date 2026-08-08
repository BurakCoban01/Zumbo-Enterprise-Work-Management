using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Relations;

internal static class UnlinkWorkItemEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id}/relations/{relatedWorkItemId}", async (
            string id,
            string relatedWorkItemId,
            string relationType,
            UnlinkWorkItemHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new UnlinkWorkItemCommand(id, relatedWorkItemId, relationType, CorrelationId(http)),
                ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemLink);
    }
}
