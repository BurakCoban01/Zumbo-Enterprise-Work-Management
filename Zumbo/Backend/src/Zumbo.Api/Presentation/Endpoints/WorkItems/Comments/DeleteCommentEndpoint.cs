using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Comments;

internal static class DeleteCommentEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id}/comments/{commentId}", async (
            string id,
            string commentId,
            DeleteCommentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await handler.HandleAsync(
                    new DeleteCommentCommand(id, commentId, CorrelationId(http)),
                    ct), http))
            .WithZumboPermission(PermissionCatalog.CommentCreate);
    }
}
