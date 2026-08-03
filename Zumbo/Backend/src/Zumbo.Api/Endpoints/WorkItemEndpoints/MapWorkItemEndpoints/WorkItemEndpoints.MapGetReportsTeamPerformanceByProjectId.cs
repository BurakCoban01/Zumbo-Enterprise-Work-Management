using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetReportsTeamPerformanceByProjectId(RouteGroupBuilder group){group.MapGet("/reports/team-performance/{projectId}", async (
            string projectId,
            DateOnly? from,
            DateOnly? to,
            WorkItemService service,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await service.TeamPerformanceSnapshotAsync(projectId, from, to, ct), http))
            .RequireRateLimiting("report");
}}
