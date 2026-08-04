using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostByIdRestore(RouteGroupBuilder group){group.MapPost("/{id}/restore", async (string id, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.RestoreAsync(id, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemDelete);
}}
