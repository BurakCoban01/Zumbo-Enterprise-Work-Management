using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Collaboration;

[ApiController]
[Route("/api/work-items/{id}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemCollaborationController : ApiControllerBase
{
    [HttpGet("collaboration")]
    public async Task<IActionResult> Get(
        [FromRoute] string id,
        [FromServices] WorkItemCollaborationService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetAsync(id, cancellationToken));

    [HttpPut("watch")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SetWatch(
        [FromRoute] string id,
        [FromBody] SetWorkItemWatchRequest request,
        [FromServices] WorkItemCollaborationService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.SetWatchingAsync(
            id,
            request.Watching,
            HttpContext.TraceIdentifier,
            cancellationToken));

    [HttpPut("vote")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SetVote(
        [FromRoute] string id,
        [FromBody] SetWorkItemVoteRequest request,
        [FromServices] WorkItemCollaborationService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.SetVoteAsync(
            id,
            request.Voted,
            HttpContext.TraceIdentifier,
            cancellationToken));
}
