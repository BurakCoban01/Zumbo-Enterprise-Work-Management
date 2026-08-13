using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Identity")]
[ZumboPermission(PermissionCatalog.UserRoleManage, isGlobal: true)]
public sealed class IdentityRoleAdministrationController : ApiControllerBase
{
    [HttpPost("roles")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request, [FromServices] IdentityAdministrationService service, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await service.CreateRoleAsync(request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPut("roles/{roleId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> UpdateRole([FromRoute] string roleId, [FromBody] UpdateRoleRequest request, [FromServices] IdentityAdministrationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpdateRoleAsync(roleId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("roles/{roleId}")]
    public async Task<IActionResult> DeleteRole([FromRoute] string roleId, [FromServices] IdentityAdministrationService service, CancellationToken cancellationToken)
    {
        await service.DeleteRoleAsync(roleId, HttpContext.TraceIdentifier, cancellationToken);
        return OkEnvelopeResult(new { deleted = true });
    }

    [HttpPut("users/{userId}/roles")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> AssignRoles([FromRoute] string userId, [FromBody] AssignUserRolesRequest request, [FromServices] IdentityAdministrationService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.AssignRolesAsync(userId, request, HttpContext.TraceIdentifier, cancellationToken));
}
