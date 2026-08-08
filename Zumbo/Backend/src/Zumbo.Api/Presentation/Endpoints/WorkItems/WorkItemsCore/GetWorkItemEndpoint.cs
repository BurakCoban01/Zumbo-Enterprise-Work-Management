using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;

internal static class GetWorkItemEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id}", async (string id, GetWorkItemHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetWorkItemQuery(id), ct), http));
    }
}
