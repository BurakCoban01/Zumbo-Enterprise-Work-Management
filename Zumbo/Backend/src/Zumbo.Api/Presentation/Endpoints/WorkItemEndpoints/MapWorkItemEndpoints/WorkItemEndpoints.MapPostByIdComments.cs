using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostByIdComments(RouteGroupBuilder group){group.MapPost("/{id}/comments", async (string id, AddCommentRequest request, AddCommentHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new AddCommentCommand(id, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.CommentCreate);
}}
