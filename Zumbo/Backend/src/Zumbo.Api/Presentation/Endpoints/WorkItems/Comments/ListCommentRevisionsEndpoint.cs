using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Comments;

internal static class ListCommentRevisionsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id}/comments/{commentId}/revisions", async (
            string id,
            string commentId,
            int? page,
            int? pageSize,
            WorkItemActivityQueryService service,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await service.ListRevisionsAsync(
                    id,
                    commentId,
                    page ?? 1,
                    pageSize ?? 50,
                    ct), http));
    }
}
