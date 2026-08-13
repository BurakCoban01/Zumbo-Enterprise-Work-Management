using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Identity.Application.Features.Mfa;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth/mfa")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Identity")]
[ZumboPermission(PermissionCatalog.ProfileRead)]
public sealed class IdentityMfaController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStatus([FromServices] GetMfaStatusHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(cancellationToken));

    [HttpPost("setup")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> BeginSetup([FromBody] BeginMfaSetupRequest request, [FromServices] BeginMfaSetupHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("confirm")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Confirm([FromBody] ConfirmMfaSetupRequest request, [FromServices] ConfirmMfaSetupHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("disable")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Disable([FromBody] DisableMfaRequest request, [FromServices] DisableMfaHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("recovery-codes")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> RegenerateRecoveryCodes([FromBody] RegenerateMfaRecoveryCodesRequest request, [FromServices] RegenerateMfaRecoveryCodesHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, HttpContext.TraceIdentifier, cancellationToken));
}
