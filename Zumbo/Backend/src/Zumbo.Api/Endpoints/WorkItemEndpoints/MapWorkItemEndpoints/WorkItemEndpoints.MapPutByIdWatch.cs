using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPutByIdWatch(RouteGroupBuilder group){group.MapPut("/{id}/watch", async (
            string id,
            SetWorkItemWatchRequest request,
            WorkItemCollaborationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetWatchingAsync(id, request.Watching, CorrelationId(http), ct), http));
}}
