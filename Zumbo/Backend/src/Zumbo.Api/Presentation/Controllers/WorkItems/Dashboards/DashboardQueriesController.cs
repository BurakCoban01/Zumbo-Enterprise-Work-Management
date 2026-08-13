using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Dashboards;

[ApiController]
[Route("/api/dashboards")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Dashboards")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class DashboardQueriesController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool? includeArchived,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] DashboardService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListAsync(
            includeArchived ?? false,
            page ?? 1,
            pageSize ?? 50,
            cancellationToken));

    [HttpGet("{dashboardId}")]
    public async Task<IActionResult> Get(
        [FromRoute] string dashboardId,
        [FromQuery] bool? includeArchived,
        [FromServices] DashboardService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetAsync(
            dashboardId,
            includeArchived ?? false,
            cancellationToken));

    [HttpGet("{dashboardId}/render")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> Render(
        [FromRoute] string dashboardId,
        [FromServices] DashboardRenderer renderer,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await renderer.RenderAsync(dashboardId, cancellationToken));
}
