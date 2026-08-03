using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostByIdChecklist(RouteGroupBuilder group){group.MapPost("/{id}/checklist", async (string id, AddChecklistItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AddChecklistItemAsync(id, request, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
