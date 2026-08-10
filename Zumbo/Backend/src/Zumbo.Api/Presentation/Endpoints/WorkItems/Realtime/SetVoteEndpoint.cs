using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Realtime;

internal static class SetVoteEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id}/vote", async (
            string id,
            SetWorkItemVoteRequest request,
            WorkItemCollaborationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetVoteAsync(id, request.Voted, CorrelationId(http), ct), http));
    }
}
