using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapDeleteByIdLabelsByLabel(RouteGroupBuilder group){group.MapDelete("/{id}/labels/{label}", async (string id, string label, RemoveLabelHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new RemoveLabelCommand(id, label), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
}}
