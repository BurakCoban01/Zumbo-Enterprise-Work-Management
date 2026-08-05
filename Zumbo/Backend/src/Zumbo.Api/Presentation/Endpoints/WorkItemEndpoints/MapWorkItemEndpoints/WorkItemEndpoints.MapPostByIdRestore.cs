using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostByIdRestore(RouteGroupBuilder group){group.MapPost("/{id}/restore", async (string id, RestoreWorkItemHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new RestoreWorkItemCommand(id, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemDelete);
}}
