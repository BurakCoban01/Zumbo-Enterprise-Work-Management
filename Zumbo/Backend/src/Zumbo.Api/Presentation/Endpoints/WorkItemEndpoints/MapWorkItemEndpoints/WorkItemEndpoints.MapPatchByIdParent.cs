using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPatchByIdParent(RouteGroupBuilder group){group.MapPatch("/{id}/parent", async (string id, SetWorkItemParentRequest request, SetParentHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new SetParentCommand(id, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
