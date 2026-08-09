using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Archive;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;

internal static class BulkArchiveWorkItemsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk/archive", async (BulkArchiveWorkItemsRequest request, BulkArchiveWorkItemsHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new BulkArchiveWorkItemsCommand(request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemDelete)
            .RequireRateLimiting("bulk");
    }
}
