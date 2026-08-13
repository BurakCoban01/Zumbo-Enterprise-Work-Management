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
public sealed class WorkItemAssignmentsController : ApiControllerBase
{
    [HttpPatch("assignee")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemAssign)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Assign(
        [FromRoute] string id,
        [FromBody] AssignWorkItemRequest request,
        [FromServices] AssignWorkItemHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new AssignWorkItemCommand(id, request, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpPatch("team")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SetTeam(
        [FromRoute] string id,
        [FromBody] SetWorkItemTeamRequest request,
        [FromServices] SetWorkItemTeamHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new SetWorkItemTeamCommand(id, request, HttpContext.TraceIdentifier),
            cancellationToken));
}
