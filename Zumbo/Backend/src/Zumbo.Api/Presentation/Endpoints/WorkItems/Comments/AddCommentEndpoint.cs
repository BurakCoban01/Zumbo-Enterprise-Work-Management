using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Comments;

internal static class AddCommentEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id}/comments", async (
            string id,
            AddCommentRequest request,
            AddCommentHandler handler,
            HttpContext http,
            CancellationToken ct) =>
                Ok(await handler.HandleAsync(
                    new AddCommentCommand(id, request, CorrelationId(http)),
                    ct), http))
            .WithZumboPermission(PermissionCatalog.CommentCreate);
    }
}
