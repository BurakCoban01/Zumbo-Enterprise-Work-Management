using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetReportsSprintVelocityByProjectId(RouteGroupBuilder group){group.MapGet("/reports/sprint-velocity/{projectId}", async (
            string projectId,
            int? sprintCount,
            SprintService service,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await service.VelocitySnapshotAsync(projectId, sprintCount ?? 6, ct), http))
            .RequireRateLimiting("report");
}}
