using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
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
public sealed class DashboardCommandsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create(
        [FromBody] SaveDashboardRequest request,
        [FromServices] DashboardService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.SaveAsync(
            null,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpPut("{dashboardId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update(
        [FromRoute] string dashboardId,
        [FromBody] SaveDashboardRequest request,
        [FromServices] DashboardService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.SaveAsync(
            dashboardId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpPut("{dashboardId}/sharing")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Share(
        [FromRoute] string dashboardId,
        [FromBody] ShareDashboardRequest request,
        [FromServices] DashboardService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ShareAsync(
            dashboardId,
            request,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpDelete("{dashboardId}")]
    public async Task<IActionResult> Archive(
        [FromRoute] string dashboardId,
        [FromServices] DashboardService service,
        CancellationToken cancellationToken)
    {
        await service.ArchiveAsync(
            dashboardId,
            HttpContext.TraceIdentifier,
            cancellationToken);
        return OkEnvelopeResult(new { archived = true });
    }
}
