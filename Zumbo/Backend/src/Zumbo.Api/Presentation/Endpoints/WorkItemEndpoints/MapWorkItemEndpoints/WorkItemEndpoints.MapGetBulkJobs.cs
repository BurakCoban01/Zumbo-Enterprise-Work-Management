using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetBulkJobs(RouteGroupBuilder group){group.MapGet("/bulk/jobs", async (
            string projectId, int? page, int? pageSize,
            WorkItemBulkJobService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListAsync(projectId, page ?? 1, pageSize ?? 50, ct), http));
}}
