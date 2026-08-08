using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetByIdActivity(RouteGroupBuilder group){group.MapGet("/{id}/activity", async (
            string id,
            int? page,
            int? pageSize,
            WorkItemCollaborationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListActivityAsync(id, page ?? 1, pageSize ?? 50, ct), http));
}}
