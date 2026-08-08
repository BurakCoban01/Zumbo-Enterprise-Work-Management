using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapDeleteByIdCommentsByCommentId(RouteGroupBuilder group){group.MapDelete("/{id}/comments/{commentId}", async (string id, string commentId, DeleteCommentHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new DeleteCommentCommand(id, commentId, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.CommentCreate);
}}
