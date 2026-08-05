using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPatchByIdChecklistByItemId(RouteGroupBuilder group){group.MapPatch("/{id}/checklist/{itemId}", async (string id, string itemId, CompleteChecklistItemRequest request, CompleteChecklistItemHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new CompleteChecklistItemCommand(id, itemId, request), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
