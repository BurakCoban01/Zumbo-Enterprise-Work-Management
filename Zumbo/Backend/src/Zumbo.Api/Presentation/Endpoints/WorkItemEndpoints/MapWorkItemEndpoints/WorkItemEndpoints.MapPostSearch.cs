using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostSearch(RouteGroupBuilder group){group.MapPost("/search", async (
            WorkItemSearchRequest request,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SearchPageAsync(request, ct), http))
            .RequireRateLimiting("search");
}}
