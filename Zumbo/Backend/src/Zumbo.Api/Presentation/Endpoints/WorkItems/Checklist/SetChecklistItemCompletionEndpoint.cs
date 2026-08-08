using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Checklist;

internal static class SetChecklistItemCompletionEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id}/checklist/{itemId}", async (
            string id,
            string itemId,
            CompleteChecklistItemRequest request,
            CompleteChecklistItemHandler handler,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await handler.HandleAsync(
                    new CompleteChecklistItemCommand(id, itemId, request),
                    ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
