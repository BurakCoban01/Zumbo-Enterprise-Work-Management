using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Comments;

internal static class EditCommentEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id}/comments/{commentId}", async (
            string id,
            string commentId,
            EditCommentRequest request,
            EditCommentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await handler.HandleAsync(
                    new EditCommentCommand(id, commentId, request, CorrelationId(http)),
                    ct), http))
            .WithZumboPermission(PermissionCatalog.CommentCreate);
    }
}
