using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Planning;

internal static class SetPlanningEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id}/planning", async (string id, SetWorkItemPlanningRequest request, SetPlanningHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new SetPlanningCommand(id, request), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
