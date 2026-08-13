using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;

namespace Zumbo.Api.Presentation.Controllers.Audit;

[ApiController]
[Route("/api/audit/")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Audit")]
[ZumboPermission(PermissionCatalog.AuditRead)]
public sealed class AuditLogQueryController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? actorUserId,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromQuery] string? organizationId,
        [FromServices] QueryAuditLogHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new AuditLogQuery(
                actorUserId,
                action,
                entityType,
                entityId,
                from,
                to,
                page ?? 1,
                pageSize ?? 50,
                cursor,
                organizationId),
            cancellationToken));
}
