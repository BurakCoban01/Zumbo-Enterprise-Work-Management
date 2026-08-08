using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPutByIdCommentsByCommentId(RouteGroupBuilder group){group.MapPut("/{id}/comments/{commentId}", async (string id, string commentId, EditCommentRequest request, EditCommentHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new EditCommentCommand(id, commentId, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.CommentCreate);
}}
