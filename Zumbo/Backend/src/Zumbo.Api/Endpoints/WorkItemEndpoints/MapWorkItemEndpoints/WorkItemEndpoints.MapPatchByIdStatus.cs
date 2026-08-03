using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPatchByIdStatus(RouteGroupBuilder group){group.MapPatch("/{id}/status", async (string id, MoveWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.MoveAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemMove);
}}
