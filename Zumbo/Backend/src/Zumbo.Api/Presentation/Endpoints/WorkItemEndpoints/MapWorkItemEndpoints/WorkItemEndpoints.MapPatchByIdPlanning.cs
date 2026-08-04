using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPatchByIdPlanning(RouteGroupBuilder group){group.MapPatch("/{id}/planning", async (string id, SetWorkItemPlanningRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.SetPlanningAsync(id, request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
