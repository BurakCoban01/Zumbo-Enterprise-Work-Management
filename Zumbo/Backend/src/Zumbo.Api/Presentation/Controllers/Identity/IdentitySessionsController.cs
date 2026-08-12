using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity.Application.Features.SessionManagement;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth/sessions")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Identity")]
[ZumboPermission(PermissionCatalog.ProfileRead)]
public sealed class IdentitySessionsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromServices] ListSessionsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(User.FindFirst("sessionId")?.Value, cancellationToken));

    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> Revoke([FromRoute] string sessionId, [FromServices] RevokeSessionHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(sessionId, HttpContext.TraceIdentifier, cancellationToken);
        return OkEnvelopeResult(new { revoked = true });
    }
}
