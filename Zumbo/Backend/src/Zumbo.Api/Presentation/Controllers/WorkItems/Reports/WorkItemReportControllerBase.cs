using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Reports;

public abstract class WorkItemReportControllerBase : ApiControllerBase
{
    protected IActionResult ReportOk<T>(WorkItemReportSnapshot<T> snapshot)
    {
        Response.Headers["X-Zumbo-Report-Generated-At"] = snapshot.GeneratedAt.ToString("O");
        Response.Headers["X-Zumbo-Report-Source-Version"] = snapshot.SourceVersion.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-Zumbo-Report-Stale"] = snapshot.Stale ? "true" : "false";
        Response.Headers["X-Zumbo-Report-Age-Seconds"] = Math.Max(
            0,
            (DateTimeOffset.UtcNow - snapshot.GeneratedAt).TotalSeconds).ToString(
                "0.###",
                CultureInfo.InvariantCulture);
        return OkEnvelopeResult(snapshot.Data);
    }
}
