using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Realtime;

internal static class SetWatchEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id}/watch", async (
            string id,
            SetWorkItemWatchRequest request,
            WorkItemCollaborationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetWatchingAsync(id, request.Watching, CorrelationId(http), ct), http));
    }
}
