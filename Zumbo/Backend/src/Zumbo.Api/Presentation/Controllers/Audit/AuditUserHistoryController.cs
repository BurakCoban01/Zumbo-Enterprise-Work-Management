using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;

namespace Zumbo.Api.Presentation.Controllers.Audit;

[ApiController]
[Route("/api/audit/user")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Audit")]
[ZumboPermission(PermissionCatalog.AuditRead)]
public sealed class AuditUserHistoryController : ApiControllerBase
{
    [HttpGet("{actorUserId}")]
    public async Task<IActionResult> Get(
        [FromRoute] string actorUserId,
        [FromServices] AuditService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListByUserAsync(actorUserId, cancellationToken));
}
