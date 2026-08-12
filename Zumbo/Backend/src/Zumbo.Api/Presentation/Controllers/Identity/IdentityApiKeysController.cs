using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth/api-keys")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Identity")]
[ZumboPermission(PermissionCatalog.ProfileRead)]
public sealed class IdentityApiKeysController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromServices] ApiKeyService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListAsync(cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request, [FromServices] ApiKeyService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.CreateAsync(request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("{apiKeyId}")]
    public async Task<IActionResult> Revoke([FromRoute] string apiKeyId, [FromServices] ApiKeyService service, CancellationToken cancellationToken)
    {
        await service.RevokeAsync(apiKeyId, HttpContext.TraceIdentifier, cancellationToken);
        return OkEnvelopeResult(new { revoked = true });
    }
}
