using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPatchByIdRank(RouteGroupBuilder group){group.MapPatch("/{id}/rank", async (string id, ReorderWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ReorderAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemMove);
}}
