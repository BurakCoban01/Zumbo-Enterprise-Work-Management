using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Relations;

internal static class LinkWorkItemEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id}/relations", async (
            string id,
            LinkWorkItemRequest request,
            LinkWorkItemHandler handler,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await handler.HandleAsync(
                    new LinkWorkItemCommand(id, request, CorrelationId(http)),
                    ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemLink);
    }
}
