using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetReportsProjectSummaryByProjectId(RouteGroupBuilder group){group.MapGet("/reports/project-summary/{projectId}", async (string projectId, WorkItemService service, HttpContext http, CancellationToken ct) =>
            ReportOk(await service.ProjectSummarySnapshotAsync(projectId, ct), http))
            .RequireRateLimiting("report");
}}
