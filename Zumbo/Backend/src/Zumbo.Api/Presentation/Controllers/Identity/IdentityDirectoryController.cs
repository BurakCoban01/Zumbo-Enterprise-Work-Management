using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Identity")]
[ZumboPermission(PermissionCatalog.ProfileRead)]
public sealed class IdentityDirectoryController : ApiControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> SearchUsers([FromQuery] string? search, [FromServices] SearchUsersHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SearchUsersQuery(search), cancellationToken));

    [HttpGet("roles")]
    public async Task<IActionResult> ListRoles([FromQuery] string? scope, [FromServices] IdentityAdministrationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListRolesAsync(cancellationToken, scope));

    [HttpGet("permissions")]
    public async Task<IActionResult> ListPermissions([FromServices] IdentityPermissionCatalogService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListAsync(cancellationToken));
}
