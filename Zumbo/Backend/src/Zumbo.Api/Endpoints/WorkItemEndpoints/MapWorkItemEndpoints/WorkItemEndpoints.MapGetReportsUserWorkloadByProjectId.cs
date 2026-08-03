using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetReportsUserWorkloadByProjectId(RouteGroupBuilder group){group.MapGet("/reports/user-workload/{projectId}", async (string projectId, WorkItemService service, HttpContext http, CancellationToken ct) =>
            ReportOk(await service.UserWorkloadSnapshotAsync(projectId, ct), http))
            .RequireRateLimiting("report");
}}
