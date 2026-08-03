using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPatchByIdChecklistByItemId(RouteGroupBuilder group){group.MapPatch("/{id}/checklist/{itemId}", async (string id, string itemId, CompleteChecklistItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.CompleteChecklistItemAsync(id, itemId, request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
