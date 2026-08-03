using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapDeleteByIdRelationsByRelatedWorkItemId(RouteGroupBuilder group){group.MapDelete("/{id}/relations/{relatedWorkItemId}", async (
            string id,
            string relatedWorkItemId,
            string relationType,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.UnlinkAsync(id, relatedWorkItemId, relationType, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemLink);
}}
