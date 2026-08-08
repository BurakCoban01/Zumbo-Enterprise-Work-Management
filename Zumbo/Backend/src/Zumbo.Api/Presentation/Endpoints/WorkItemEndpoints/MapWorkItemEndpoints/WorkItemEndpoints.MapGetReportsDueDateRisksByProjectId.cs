using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetReportsDueDateRisksByProjectId(RouteGroupBuilder group){group.MapGet("/reports/due-date-risks/{projectId}", async (string projectId, int? days, DueDateRisksHandler handler, HttpContext http, CancellationToken ct) =>
            ReportOk(await handler.HandleAsync(new DueDateRisksQuery(projectId, days ?? 14), ct), http))
            .RequireRateLimiting("report");
}}
