using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetReportsSprintBurndownByProjectIdBySprintId(RouteGroupBuilder group){group.MapGet("/reports/sprint-burndown/{projectId}/{sprintId}", async (
            string projectId,
            string sprintId,
            DateOnly startDate,
            DateOnly endDate,
            SprintService service,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await service.BurndownSnapshotAsync(projectId, sprintId, startDate, endDate, ct), http))
            .RequireRateLimiting("report");
}}
