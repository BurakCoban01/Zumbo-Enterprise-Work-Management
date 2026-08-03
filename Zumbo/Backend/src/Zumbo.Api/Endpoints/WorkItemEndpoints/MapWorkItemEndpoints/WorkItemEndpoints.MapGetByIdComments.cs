using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetByIdComments(RouteGroupBuilder group){group.MapGet("/{id}/comments", async (string id, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListCommentsAsync(id, page ?? 1, pageSize ?? 50, ct), http));
}}
