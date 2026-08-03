using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapPostByIdComments(RouteGroupBuilder group){group.MapPost("/{id}/comments", async (string id, AddCommentRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.AddCommentAsync(id, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.CommentCreate);
}}
