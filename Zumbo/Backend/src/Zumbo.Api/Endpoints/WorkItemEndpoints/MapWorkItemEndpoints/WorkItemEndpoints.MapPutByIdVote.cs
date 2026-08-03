using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPutByIdVote(RouteGroupBuilder group){group.MapPut("/{id}/vote", async (
            string id,
            SetWorkItemVoteRequest request,
            WorkItemCollaborationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetVoteAsync(id, request.Voted, CorrelationId(http), ct), http));
}}
