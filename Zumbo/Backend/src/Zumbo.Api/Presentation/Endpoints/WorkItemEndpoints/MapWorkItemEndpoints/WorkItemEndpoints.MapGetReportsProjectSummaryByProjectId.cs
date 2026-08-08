using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetReportsProjectSummaryByProjectId(RouteGroupBuilder group){group.MapGet("/reports/project-summary/{projectId}", async (string projectId, ProjectSummaryHandler handler, HttpContext http, CancellationToken ct) =>
            ReportOk(await handler.HandleAsync(new ProjectSummaryQuery(projectId), ct), http))
            .RequireRateLimiting("report");
}}
