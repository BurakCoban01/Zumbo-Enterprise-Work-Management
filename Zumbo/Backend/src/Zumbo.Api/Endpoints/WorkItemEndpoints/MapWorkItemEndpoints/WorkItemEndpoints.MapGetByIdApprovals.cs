using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetByIdApprovals(RouteGroupBuilder group){group.MapGet("/{id}/approvals", async (string id, int? page, int? pageSize, WorkItemActivityQueryService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListApprovalsAsync(id, page ?? 1, pageSize ?? 50, ct), http));
}}
