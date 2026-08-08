using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Comments;

internal static class ListCommentsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id}/comments", async (
            string id,
            int? page,
            int? pageSize,
            WorkItemActivityQueryService service,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await service.ListCommentsAsync(id, page ?? 1, pageSize ?? 50, ct), http));
    }
}
