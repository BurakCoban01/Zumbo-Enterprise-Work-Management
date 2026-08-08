using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{
private static void MapGetReportsCompletionRateByProjectId(RouteGroupBuilder group){group.MapGet("/reports/completion-rate/{projectId}", async (
            string projectId,
            DateOnly? from,
            DateOnly? to,
            CompletionRateHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await handler.HandleAsync(new CompletionRateQuery(projectId, from, to), ct), http))
            .RequireRateLimiting("report");
}}
