using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.Api.Presentation.Controllers.Platform;

[ApiController]
[Route("/api/operations/storage/security")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Operations")]
[ZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)]
public sealed class OperationsStorageSecurityController : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("report")]
    public async Task<IActionResult> GetStatus(
        [MinimalApiRequiredQuery] string organizationId,
        [FromServices] OperationsStorageSecurityCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        if (!Request.Query.ContainsKey(nameof(organizationId)))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new EmptyResult();
        }

        return Ok(await coordinator.GetStatusAsync(organizationId, cancellationToken));
    }

    [HttpPost("maintenance")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> RunMaintenance(
        [MinimalApiRequiredQuery] string organizationId,
        [FromServices] OperationsStorageSecurityCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        if (!Request.Query.ContainsKey(nameof(organizationId)))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new EmptyResult();
        }

        return Ok(await coordinator.RunAsync(
            organizationId,
            HttpContext.TraceIdentifier,
            cancellationToken));
    }
}
