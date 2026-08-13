using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Core;

[ApiController]
[Route("/api/work-items/{id}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemLifecycleController : ApiControllerBase
{
    [HttpPatch("status")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemMove)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Move([FromRoute] string id, [FromBody] MoveWorkItemRequest request, [FromServices] MoveWorkItemHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new MoveWorkItemCommand(id, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpDelete]
    [ZumboPermission(PermissionCatalog.WorkItemDelete)]
    public async Task<IActionResult> Archive([FromRoute] string id, [FromServices] ArchiveWorkItemHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new ArchiveWorkItemCommand(id, HttpContext.TraceIdentifier), cancellationToken);
        return OkEnvelopeResult(new { archived = true });
    }

    [HttpPost("restore")]
    [ZumboPermission(PermissionCatalog.WorkItemDelete)]
    public async Task<IActionResult> Restore([FromRoute] string id, [FromServices] RestoreWorkItemHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new RestoreWorkItemCommand(id, HttpContext.TraceIdentifier), cancellationToken));
}
