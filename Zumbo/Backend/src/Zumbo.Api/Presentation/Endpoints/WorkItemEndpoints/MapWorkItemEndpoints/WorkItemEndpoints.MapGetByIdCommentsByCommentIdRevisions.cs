using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetByIdCommentsByCommentIdRevisions(RouteGroupBuilder group){group.MapGet("/{id}/comments/{commentId}/revisions", async (string id, string commentId, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListRevisionsAsync(id, commentId, page ?? 1, pageSize ?? 50, ct), http));
}}
