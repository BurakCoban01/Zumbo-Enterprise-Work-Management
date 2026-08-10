using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Search;

internal static class SearchWorkItemsPageEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/search", async (
            WorkItemSearchRequest request,
            SearchWorkItemsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandlePageAsync(request, ct), http))
            .RequireRateLimiting("search");
    }
}
