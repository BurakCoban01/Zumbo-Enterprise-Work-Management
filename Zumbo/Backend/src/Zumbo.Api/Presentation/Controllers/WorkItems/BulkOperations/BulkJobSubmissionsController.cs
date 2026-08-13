using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
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
public sealed class BulkJobSubmissionsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [EnableRateLimiting("bulk")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] CreateWorkItemBulkJobRequest request, [FromServices] WorkItemBulkJobService service, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await service.SubmitBulkAsync(request, IdempotencyKey(), HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("export")]
    [Consumes("application/json")]
    [EnableRateLimiting("bulk")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Export([FromBody] CreateWorkItemExportJobRequest request, [FromServices] WorkItemBulkJobService service, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await service.SubmitExportAsync(request, IdempotencyKey(), HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("import")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemCreate)]
    [EnableRateLimiting("bulk")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Import([FromBody] CreateWorkItemImportJobRequest request, [FromServices] WorkItemBulkJobService service, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await service.SubmitImportAsync(request, IdempotencyKey(), HttpContext.TraceIdentifier, cancellationToken));

    private string IdempotencyKey() => Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
}
