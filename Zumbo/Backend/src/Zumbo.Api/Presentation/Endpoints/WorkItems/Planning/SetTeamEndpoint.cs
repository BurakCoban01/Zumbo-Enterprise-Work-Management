using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Planning;

internal static class SetTeamEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id}/team", async (string id, SetWorkItemTeamRequest request, SetWorkItemTeamHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new SetWorkItemTeamCommand(id, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
