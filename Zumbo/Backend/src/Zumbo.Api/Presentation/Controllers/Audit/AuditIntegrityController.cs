using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;

namespace Zumbo.Api.Presentation.Controllers.Audit;

[ApiController]
[Route("/api/audit/integrity")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Audit")]
[ZumboPermission(PermissionCatalog.AuditReadAll, isGlobal: true)]
public sealed class AuditIntegrityController : ApiControllerBase
{
    [HttpGet("{organizationId}")]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> Get(
        [FromRoute] string organizationId,
        [FromServices] AuditService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.VerifyIntegrityAsync(organizationId, cancellationToken));
}
