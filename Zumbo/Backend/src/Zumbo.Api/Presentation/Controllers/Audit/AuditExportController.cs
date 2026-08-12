using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;

namespace Zumbo.Api.Presentation.Controllers.Audit;

[ApiController]
[Route("/api/audit/export")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Audit")]
[ZumboPermission(PermissionCatalog.AuditRead)]
public sealed class AuditExportController : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> Get(
        [FromQuery] string? actorUserId,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? organizationId,
        [FromServices] AuditService service,
        CancellationToken cancellationToken)
    {
        var records = await service.ExportAsync(
            new AuditLogQuery(actorUserId, action, entityType, entityId, from, to, OrganizationId: organizationId),
            cancellationToken);
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson";
        Response.Headers.ContentDisposition = "attachment; filename=zumbo-audit-export.ndjson";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Zumbo-Export-Format"] = "audit-ndjson-v1";
        Response.Headers["X-Zumbo-Export-Records"] =
            records.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await AuditService.WriteNdjsonAsync(records, Response.Body, cancellationToken);
        return new EmptyResult();
    }
}
