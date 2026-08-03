using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapDeleteById(RouteGroupBuilder group){group.MapDelete("/{id}", async (string id, WorkItemService service, HttpContext http, CancellationToken ct) =>
        {
            await service.ArchiveAsync(id, CorrelationId(http), ct);
            return Ok(new { archived = true }, http);
        }).WithZumboPermission(PermissionCatalog.WorkItemDelete);
}}
