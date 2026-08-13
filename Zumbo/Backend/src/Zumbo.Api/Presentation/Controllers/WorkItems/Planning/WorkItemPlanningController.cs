using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Planning;

[ApiController]
[Route("/api/work-items/{id}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemPlanningController : ApiControllerBase
{
    [HttpPatch("planning")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SetPlanning(
        [FromRoute] string id,
        [FromBody] SetWorkItemPlanningRequest request,
        [FromServices] SetPlanningHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SetPlanningCommand(id, request), cancellationToken));

    [HttpPatch("parent")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SetParent(
        [FromRoute] string id,
        [FromBody] SetWorkItemParentRequest request,
        [FromServices] SetParentHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new SetParentCommand(id, request, HttpContext.TraceIdentifier),
            cancellationToken));
}
