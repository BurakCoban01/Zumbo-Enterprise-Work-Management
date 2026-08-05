using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostByIdLabels(RouteGroupBuilder group){group.MapPost("/{id}/labels", async (string id, AddLabelRequest request, AddLabelHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new AddLabelCommand(id, request), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
