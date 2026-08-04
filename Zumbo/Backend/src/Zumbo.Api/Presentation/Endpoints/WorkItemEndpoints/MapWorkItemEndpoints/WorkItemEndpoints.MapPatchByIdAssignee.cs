using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPatchByIdAssignee(RouteGroupBuilder group){group.MapPatch("/{id}/assignee", async (string id, AssignWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AssignAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemAssign);
}}
