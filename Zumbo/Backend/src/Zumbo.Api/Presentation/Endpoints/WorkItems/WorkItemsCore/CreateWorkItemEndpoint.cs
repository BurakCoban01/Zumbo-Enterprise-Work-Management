using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;

internal static class CreateWorkItemEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateWorkItemRequest request, CreateWorkItemHandler handler, HttpContext http, CancellationToken ct) =>
            Created(await handler.HandleAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemCreate);
    }
}
