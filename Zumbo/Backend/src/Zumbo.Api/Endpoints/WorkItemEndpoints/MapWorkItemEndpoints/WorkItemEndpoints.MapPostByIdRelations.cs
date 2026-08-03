using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostByIdRelations(RouteGroupBuilder group){group.MapPost("/{id}/relations", async (string id, LinkWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.LinkAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemLink);
}}
