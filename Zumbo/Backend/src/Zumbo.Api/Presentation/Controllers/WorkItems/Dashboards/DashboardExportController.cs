using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Dashboards;

[ApiController]
[Route("/api/dashboards/{dashboardId}/export")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Dashboards")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class DashboardExportController : ApiControllerBase
{
    [HttpGet]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> Export([FromRoute] string dashboardId, [FromServices] DashboardService service, CancellationToken cancellationToken)
    {
        var dashboard = await service.GetAsync(dashboardId, includeArchived: false, cancellationToken);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            dashboard,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return File(bytes, "application/json", $"zumbo-dashboard-{dashboard.Id}.json");
    }
}
