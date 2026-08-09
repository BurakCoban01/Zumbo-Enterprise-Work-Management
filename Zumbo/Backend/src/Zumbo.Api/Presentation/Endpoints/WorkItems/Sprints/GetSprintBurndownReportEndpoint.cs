using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Sprints;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;
using static Zumbo.Api.Presentation.Endpoints.WorkItems.Reports.WorkItemReportEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Sprints;

internal static class GetSprintBurndownReportEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/reports/sprint-burndown/{projectId}/{sprintId}", async (
            string projectId,
            string sprintId,
            DateOnly startDate,
            DateOnly endDate,
            GetSprintBurndownHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            ReportOk(await handler.HandleAsync(
                new GetSprintBurndownQuery(projectId, sprintId, startDate, endDate),
                ct), http))
            .RequireRateLimiting("report");
    }

    private static IResult ReportOk<T>(WorkItemReportSnapshot<T> snapshot, HttpContext http)
    {
        http.Response.Headers["X-Zumbo-Report-Generated-At"] = snapshot.GeneratedAt.ToString("O");
        http.Response.Headers["X-Zumbo-Report-Source-Version"] = snapshot.SourceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        http.Response.Headers["X-Zumbo-Report-Stale"] = snapshot.Stale ? "true" : "false";
        http.Response.Headers["X-Zumbo-Report-Age-Seconds"] = Math.Max(0, (DateTimeOffset.UtcNow - snapshot.GeneratedAt).TotalSeconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return Ok(snapshot.Data, http);
    }
}
