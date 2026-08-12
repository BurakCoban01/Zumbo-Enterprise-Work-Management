using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth/privacy")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Identity")]
[ZumboPermission(PermissionCatalog.ProfileRead)]
public sealed class IdentityPrivacyExportController : ApiControllerBase
{
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromServices] PrivacyService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ExportAsync(cancellationToken));

    [HttpGet("export.ndjson")]
    public async Task<IActionResult> StreamExport([FromServices] PrivacyService service, CancellationToken cancellationToken)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson";
        Response.Headers.ContentDisposition = "attachment; filename=zumbo-privacy-export.ndjson";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Zumbo-Export-Format"] = "ndjson-v1";
        _ = await service.StreamExportAsync(Response.Body, cancellationToken);
        return new EmptyResult();
    }
}
