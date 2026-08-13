using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.CapacityPlanning;

[ApiController]
[Route("/api/capacity-plans")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("CapacityPlanning")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class CapacityPlanCommandsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create(
        [FromBody] SaveCapacityPlanRequest request,
        [FromServices] SaveCapacityPlanHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new SaveCapacityPlanCommand(null, request, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpPut("{planId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update(
        [FromRoute] string planId,
        [FromBody] SaveCapacityPlanRequest request,
        [FromServices] SaveCapacityPlanHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new SaveCapacityPlanCommand(planId, request, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpPut("{planId}/sharing")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Share(
        [FromRoute] string planId,
        [FromBody] ShareCapacityPlanRequest request,
        [FromServices] ShareCapacityPlanHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new ShareCapacityPlanCommand(planId, request, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpDelete("{planId}")]
    public async Task<IActionResult> Archive(
        [FromRoute] string planId,
        [FromServices] ArchiveCapacityPlanHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(
            new ArchiveCapacityPlanCommand(planId, HttpContext.TraceIdentifier),
            cancellationToken);
        return OkEnvelopeResult(new { archived = true });
    }
}
