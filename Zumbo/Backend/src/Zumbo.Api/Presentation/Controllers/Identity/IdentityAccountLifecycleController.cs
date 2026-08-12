using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity.Application.Features.AccountLifecycle;
using Zumbo.Modules.Identity.Application.Features.PasswordChange;
using Zumbo.Modules.Identity.Application.Features.PasswordReset;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth")]
[EnableRateLimiting("api")]
[Tags("Identity")]
public sealed class IdentityAccountLifecycleController : ApiControllerBase
{
    [HttpPost("change-password")]
    [Authorize]
    [ZumboPermission(PermissionCatalog.ProfileRead)]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, [FromServices] ChangePasswordHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("forgot-password")]
    [Consumes("application/json")]
    [EnableRateLimiting("password-reset")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, [FromServices] ForgotPasswordHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, cancellationToken));

    [HttpPost("reset-password")]
    [Consumes("application/json")]
    [EnableRateLimiting("password-reset")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, [FromServices] ResetPasswordHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("deactivate")]
    [Authorize]
    [ZumboPermission(PermissionCatalog.ProfileRead)]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Deactivate([FromBody] DeactivateAccountRequest request, [FromServices] DeactivateAccountHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, cancellationToken));
}
