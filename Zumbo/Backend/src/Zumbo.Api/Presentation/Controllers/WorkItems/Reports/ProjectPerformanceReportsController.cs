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
public sealed class ProjectPerformanceReportsController : WorkItemReportControllerBase
{
    [HttpGet("flow-time/{projectId}")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetFlowTime([FromRoute] string projectId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromServices] FlowTimeHandler handler, CancellationToken cancellationToken) =>
        ReportOk(await handler.HandleAsync(new FlowTimeQuery(projectId, from, to), cancellationToken));

    [HttpGet("completion-rate/{projectId}")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetCompletionRate([FromRoute] string projectId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromServices] CompletionRateHandler handler, CancellationToken cancellationToken) =>
        ReportOk(await handler.HandleAsync(new CompletionRateQuery(projectId, from, to), cancellationToken));

    [HttpGet("team-performance/{projectId}")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetTeamPerformance([FromRoute] string projectId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromServices] TeamPerformanceHandler handler, CancellationToken cancellationToken) =>
        ReportOk(await handler.HandleAsync(new TeamPerformanceQuery(projectId, from, to), cancellationToken));
}
