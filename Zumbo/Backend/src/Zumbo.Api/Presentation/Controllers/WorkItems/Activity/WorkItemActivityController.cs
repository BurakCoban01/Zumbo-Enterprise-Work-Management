using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Activity;

[ApiController]
[Route("/api/work-items/{id}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemActivityController : ApiControllerBase
{
    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity(
        [FromRoute] string id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] WorkItemCollaborationService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListActivityAsync(id, page ?? 1, pageSize ?? 50, cancellationToken));

    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline(
        [FromRoute] string id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] WorkItemActivityQueryService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListTimelineAsync(id, page ?? 1, pageSize ?? 50, cancellationToken));
}
