using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Sprints;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Sprints;

[ApiController]
[Route("/api/sprints/{sprintId}/items/{workItemId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Sprints")]
[ZumboPermission(PermissionCatalog.WorkItemUpdate)]
[DurableTransaction("WorkItems")]
public sealed class SprintScopeController : ApiControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Plan([FromRoute] string sprintId, [FromRoute] string workItemId, [FromBody] PlanSprintWorkItemRequest request, [FromServices] PlanSprintWorkItemHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new PlanSprintWorkItemCommand(sprintId, workItemId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpDelete]
    public async Task<IActionResult> Unplan([FromRoute] string sprintId, [FromRoute] string workItemId, [FromServices] UnplanSprintWorkItemHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new UnplanSprintWorkItemCommand(sprintId, workItemId, HttpContext.TraceIdentifier), cancellationToken));
}
