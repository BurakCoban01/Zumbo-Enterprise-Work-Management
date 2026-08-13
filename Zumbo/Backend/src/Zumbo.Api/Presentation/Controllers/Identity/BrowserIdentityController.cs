using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/browser-auth")]
[EnableRateLimiting("api")]
[Tags("BrowserIdentity")]
public sealed class BrowserIdentityController : ApiControllerBase
{
    [HttpPost("register")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Register([FromBody] RegisterUserRequest request, [FromServices] BrowserSessionService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RegisterAsync(request, HttpContext, cancellationToken));

    [HttpPost("login")]
    [Consumes("application/json")]
    [EnableRateLimiting("login")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, [FromServices] BrowserSessionService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.LoginAsync(request, HttpContext, cancellationToken));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromServices] BrowserSessionService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RefreshAsync(HttpContext, cancellationToken));

    [HttpPost("logout")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Logout([FromBody] BrowserLogoutRequest request, [FromServices] BrowserSessionService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.LogoutAsync(request, HttpContext, cancellationToken));

    [HttpGet("session")]
    [Authorize]
    [ZumboPermission(PermissionCatalog.ProfileRead)]
    public async Task<IActionResult> GetSession([FromServices] BrowserSessionService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetSessionAsync(HttpContext, cancellationToken));
}
