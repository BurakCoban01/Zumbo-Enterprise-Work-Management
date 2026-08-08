using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;

internal static class ArchiveWorkItemEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id}", async (string id, ArchiveWorkItemHandler handler, HttpContext http, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ArchiveWorkItemCommand(id, CorrelationId(http)), ct);
            return Ok(new { archived = true }, http);
        }).WithZumboPermission(PermissionCatalog.WorkItemDelete);
    }
}
