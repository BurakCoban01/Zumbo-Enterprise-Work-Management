using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetReportsStatusDistributionByProjectId(RouteGroupBuilder group){group.MapGet("/reports/status-distribution/{projectId}", async (string projectId, StatusDistributionHandler handler, HttpContext http, CancellationToken ct) =>
            ReportOk(await handler.HandleAsync(new StatusDistributionQuery(projectId), ct), http))
            .RequireRateLimiting("report");
}}
