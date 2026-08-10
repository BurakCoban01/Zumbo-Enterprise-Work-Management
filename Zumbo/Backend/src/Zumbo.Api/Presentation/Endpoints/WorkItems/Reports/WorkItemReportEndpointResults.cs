using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Reports;

internal static class WorkItemReportEndpointResults
{
    internal static IResult ReportOk<T>(WorkItemReportSnapshot<T> snapshot, HttpContext http)
    {
        http.Response.Headers["X-Zumbo-Report-Generated-At"] = snapshot.GeneratedAt.ToString("O");
        http.Response.Headers["X-Zumbo-Report-Source-Version"] = snapshot.SourceVersion.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        http.Response.Headers["X-Zumbo-Report-Stale"] = snapshot.Stale ? "true" : "false";
        http.Response.Headers["X-Zumbo-Report-Age-Seconds"] = Math.Max(
            0,
            (DateTimeOffset.UtcNow - snapshot.GeneratedAt).TotalSeconds).ToString(
                "0.###",
                System.Globalization.CultureInfo.InvariantCulture);
        return Ok(snapshot.Data, http);
    }
}
