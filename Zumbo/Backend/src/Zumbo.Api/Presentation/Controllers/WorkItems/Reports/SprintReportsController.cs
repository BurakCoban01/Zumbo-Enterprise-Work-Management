using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Sprints;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Reports;

[ApiController]
[Route("/api/work-items/reports")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class SprintReportsController : WorkItemReportControllerBase
{
    [HttpGet("sprint-burndown/{projectId}/{sprintId}")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetBurndown(
        [FromRoute] string projectId,
        [FromRoute] string sprintId,
        [FromQuery, BindRequired] DateOnly startDate,
        [FromQuery, BindRequired] DateOnly endDate,
        [FromServices] GetSprintBurndownHandler handler,
        CancellationToken cancellationToken) =>
        ReportOk(await handler.HandleAsync(
            new GetSprintBurndownQuery(projectId, sprintId, startDate, endDate),
            cancellationToken));

    [HttpGet("sprint-velocity/{projectId}")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetVelocity(
        [FromRoute] string projectId,
        [FromQuery] int? sprintCount,
        [FromServices] GetSprintVelocityHandler handler,
        CancellationToken cancellationToken) =>
        ReportOk(await handler.HandleAsync(
            new GetSprintVelocityQuery(projectId, sprintCount ?? 6),
            cancellationToken));
}
