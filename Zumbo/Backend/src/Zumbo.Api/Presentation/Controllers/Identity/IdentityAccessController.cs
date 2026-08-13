using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Modules.Identity.Application.Features.Login;
using Zumbo.Modules.Identity.Application.Features.Logout;
using Zumbo.Modules.Identity.Application.Features.TokenRefresh;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth")]
[EnableRateLimiting("api")]
[Tags("Identity")]
public sealed class IdentityAccessController : ApiControllerBase
{
    [HttpPost("register")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Register([FromBody] RegisterUserRequest request, [FromServices] RegisterUserHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, cancellationToken));

    [HttpPost("login")]
    [Consumes("application/json")]
    [EnableRateLimiting("login")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, [FromServices] LoginHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, cancellationToken));

    [HttpPost("refresh")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, [FromServices] RefreshTokenHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, cancellationToken));

    [HttpPost("logout")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, [FromServices] LogoutHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request, cancellationToken));
}
