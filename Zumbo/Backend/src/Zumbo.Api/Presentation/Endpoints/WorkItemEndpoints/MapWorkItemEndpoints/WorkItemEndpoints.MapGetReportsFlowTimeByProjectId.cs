using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetReportsFlowTimeByProjectId(RouteGroupBuilder group){group.MapGet("/reports/flow-time/{projectId}", async (
            string projectId,
            DateOnly? from,
            DateOnly? to,
            FlowTimeHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await handler.HandleAsync(new FlowTimeQuery(projectId, from, to), ct), http))
            .RequireRateLimiting("report");
}}
