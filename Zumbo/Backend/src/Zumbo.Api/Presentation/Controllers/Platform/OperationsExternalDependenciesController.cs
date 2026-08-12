using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.Api.Presentation.Controllers.Platform;

[ApiController]
[Route("/api/operations")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Operations")]
[ZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)]
public sealed class OperationsExternalDependenciesController : ControllerBase
{
    [HttpGet("external-dependencies")]
    [EnableRateLimiting("report")]
    public IActionResult Get([FromServices] IExternalDependencyPolicyProvider policies)
    {
        var captured = policies.GetSnapshots().ToDictionary(snapshot => snapshot.Dependency, StringComparer.Ordinal);
        var dependencies = ExternalDependencyNames.All.Select(name =>
            captured.TryGetValue(name, out var snapshot)
                ? snapshot
                : new ExternalDependencySnapshot(
                    name, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0));

        return Ok(new
        {
            status = dependencies.Any(snapshot => snapshot.CircuitOpen) ? "degraded" : "available",
            capturedAtUtc = DateTimeOffset.UtcNow,
            dependencies
        });
    }
}
