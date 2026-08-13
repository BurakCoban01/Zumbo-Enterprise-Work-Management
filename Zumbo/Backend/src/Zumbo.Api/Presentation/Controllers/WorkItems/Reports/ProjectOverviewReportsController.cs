using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Reports;

[ApiController]
[Route("/api/work-items/reports")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class ProjectOverviewReportsController : WorkItemReportControllerBase
{
    [HttpGet("project-summary/{projectId}")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetProjectSummary([FromRoute] string projectId, [FromServices] ProjectSummaryHandler handler, CancellationToken cancellationToken) =>
        ReportOk(await handler.HandleAsync(new ProjectSummaryQuery(projectId), cancellationToken));

    [HttpGet("status-distribution/{projectId}")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetStatusDistribution([FromRoute] string projectId, [FromServices] StatusDistributionHandler handler, CancellationToken cancellationToken) =>
        ReportOk(await handler.HandleAsync(new StatusDistributionQuery(projectId), cancellationToken));

    [HttpGet("user-workload/{projectId}")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetUserWorkload([FromRoute] string projectId, [FromServices] UserWorkloadHandler handler, CancellationToken cancellationToken) =>
        ReportOk(await handler.HandleAsync(new UserWorkloadQuery(projectId), cancellationToken));

    [HttpGet("due-date-risks/{projectId}")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetDueDateRisks([FromRoute] string projectId, [FromQuery] int? days, [FromServices] DueDateRisksHandler handler, CancellationToken cancellationToken) =>
        ReportOk(await handler.HandleAsync(new DueDateRisksQuery(projectId, days ?? 14), cancellationToken));
}
