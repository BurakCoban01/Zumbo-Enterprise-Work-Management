using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Archive;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Assign;
using Zumbo.Modules.WorkItems.Application.Features.BulkOperations.Move;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.BulkOperations;

[ApiController]
[Route("/api/work-items/bulk")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class BulkWorkItemCommandsController : ApiControllerBase
{
    [HttpPost("move")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemMove)]
    [EnableRateLimiting("bulk")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Move([FromBody] BulkMoveWorkItemsRequest request, [FromServices] BulkMoveWorkItemsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new BulkMoveWorkItemsCommand(request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("assign")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemAssign)]
    [EnableRateLimiting("bulk")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Assign([FromBody] BulkAssignWorkItemsRequest request, [FromServices] BulkAssignWorkItemsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new BulkAssignWorkItemsCommand(request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("archive")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemDelete)]
    [EnableRateLimiting("bulk")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Archive([FromBody] BulkArchiveWorkItemsRequest request, [FromServices] BulkArchiveWorkItemsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new BulkArchiveWorkItemsCommand(request, HttpContext.TraceIdentifier), cancellationToken));
}
