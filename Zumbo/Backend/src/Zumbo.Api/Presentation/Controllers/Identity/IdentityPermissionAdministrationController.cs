using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth/permissions")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Identity")]
[ZumboPermission(PermissionCatalog.UserRoleManage, isGlobal: true)]
public sealed class IdentityPermissionAdministrationController : ApiControllerBase
{
    [HttpPut("{key}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string key, [FromBody] UpdatePermissionDefinitionRequest request, [FromServices] IdentityPermissionCatalogService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpdateAsync(key, request, HttpContext.TraceIdentifier, cancellationToken));
}
