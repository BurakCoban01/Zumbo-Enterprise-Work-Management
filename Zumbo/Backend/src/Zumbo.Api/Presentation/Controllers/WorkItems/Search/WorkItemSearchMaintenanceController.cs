using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Search;

[ApiController]
[Route("/api/work-items/search")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemSearchMaintenanceController : ControllerBase
{
    [HttpPost("rebuild")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> Rebuild(
        [FromServices] SearchMaintenanceService service,
        [FromServices] IWorkItemOperationsAuditWriter audit,
        CancellationToken cancellationToken)
    {
        var result = await service.RebuildAsync(cancellationToken);
        await audit.WriteAsync(
            "SearchIndexRebuilt",
            "Operations",
            "work-item-search",
            null,
            $"{result.Indexed}:{result.Removed}:{result.AliasChanged}",
            HttpContext.TraceIdentifier,
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("reconcile")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> Reconcile(
        [FromServices] SearchMaintenanceService service,
        [FromServices] IWorkItemOperationsAuditWriter audit,
        CancellationToken cancellationToken)
    {
        var result = await service.RebuildAsync(cancellationToken);
        await audit.WriteAsync(
            "SearchIndexReconciled",
            "Operations",
            "work-item-search",
            null,
            $"{result.Indexed}:{result.Removed}:{result.AliasChanged}",
            HttpContext.TraceIdentifier,
            cancellationToken);
        return Ok(result);
    }
}
