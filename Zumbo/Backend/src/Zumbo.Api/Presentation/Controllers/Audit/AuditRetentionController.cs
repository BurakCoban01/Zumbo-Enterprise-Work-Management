using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Presentation.Controllers.Audit;

[ApiController]
[Route("/api/audit/retention")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Audit")]
[ZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)]
public sealed class AuditRetentionController : ControllerBase
{
    [HttpPost("purge")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> Purge(
        [MinimalApiRequiredQuery] string organizationId,
        [FromServices] AuditService service,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        if (!Request.Query.ContainsKey(nameof(organizationId)))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new EmptyResult();
        }

        return Ok(await service.PurgeExpiredAsync(organizationId, clock.UtcNow, cancellationToken));
    }
}
