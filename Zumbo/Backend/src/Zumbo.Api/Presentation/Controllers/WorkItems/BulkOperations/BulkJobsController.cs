using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.BulkOperations;

[ApiController]
[Route("/api/work-items/bulk/jobs")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class BulkJobsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery, BindRequired] string projectId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] WorkItemBulkJobService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListAsync(projectId, page ?? 1, pageSize ?? 50, cancellationToken));

    [HttpGet("{jobId}")]
    public async Task<IActionResult> Get([FromRoute] string jobId, [FromServices] WorkItemBulkJobService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetAsync(jobId, cancellationToken));

    [HttpPost("{jobId}/cancel")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    public async Task<IActionResult> Cancel([FromRoute] string jobId, [FromServices] WorkItemBulkJobService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.CancelAsync(jobId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("{jobId}/retry")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    public async Task<IActionResult> Retry([FromRoute] string jobId, [FromServices] WorkItemBulkJobService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RetryAsync(jobId, HttpContext.TraceIdentifier, cancellationToken));
}
