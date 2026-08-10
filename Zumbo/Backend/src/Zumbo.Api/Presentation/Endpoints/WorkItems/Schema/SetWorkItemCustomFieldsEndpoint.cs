using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Schema;

internal static class SetWorkItemCustomFieldsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id}/custom-fields", async (
                string id,
                SetWorkItemCustomFieldsRequest request,
                SetCustomFieldsHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(new SetCustomFieldsCommand(id, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);
    }
}
