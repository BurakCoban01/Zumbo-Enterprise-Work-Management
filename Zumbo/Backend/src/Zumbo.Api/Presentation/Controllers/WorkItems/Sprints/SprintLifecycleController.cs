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
[Route("/api/sprints")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Sprints")]
[ZumboPermission(PermissionCatalog.WorkItemUpdate)]
[DurableTransaction("WorkItems")]
public sealed class SprintLifecycleController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] CreateSprintRequest request, [FromServices] CreateSprintHandler handler, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(new CreateSprintCommand(request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("{sprintId}/start")]
    public async Task<IActionResult> Start([FromRoute] string sprintId, [FromServices] StartSprintHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new StartSprintCommand(sprintId, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("{sprintId}/complete")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Complete([FromRoute] string sprintId, [FromBody] CompleteSprintRequest request, [FromServices] CompleteSprintHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new CompleteSprintCommand(sprintId, request, HttpContext.TraceIdentifier), cancellationToken));
}
