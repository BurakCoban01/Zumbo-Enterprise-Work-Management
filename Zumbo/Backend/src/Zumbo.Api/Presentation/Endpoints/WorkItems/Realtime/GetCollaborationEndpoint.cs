using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Realtime;

internal static class GetCollaborationEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id}/collaboration", async (
            string id,
            WorkItemCollaborationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(id, ct), http));
    }
}
