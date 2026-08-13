using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Scenarios;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Snapshots;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.CapacityPlanning;

[ApiController]
[Route("/api/capacity-plans")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("CapacityPlanning")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class CapacityPlanQueriesController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool? includeArchived,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] ListCapacityPlansHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new ListCapacityPlansQuery(includeArchived ?? false, page ?? 1, pageSize ?? 50),
            cancellationToken));

    [HttpGet("{planId}")]
    public async Task<IActionResult> Get(
        [FromRoute] string planId,
        [FromQuery] bool? includeArchived,
        [FromServices] GetCapacityPlanHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new GetCapacityPlanQuery(planId, includeArchived ?? false),
            cancellationToken));

    [HttpGet("{planId}/snapshot")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> Snapshot(
        [FromRoute] string planId,
        [FromServices] GetCapacitySnapshotHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new GetCapacitySnapshotQuery(planId),
            cancellationToken));

    [HttpPost("{planId}/scenarios")]
    [Consumes("application/json")]
    [EnableRateLimiting("report")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> PreviewScenario(
        [FromRoute] string planId,
        [FromBody] CapacityScenarioRequest request,
        [FromServices] PreviewScenarioHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new PreviewScenarioQuery(planId, request),
            cancellationToken));
}
