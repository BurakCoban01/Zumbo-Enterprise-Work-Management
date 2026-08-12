using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;

namespace Zumbo.Api.Presentation.Controllers.Projects;

[ApiController]
[Route("/api/projects/{projectId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Projects")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class ProjectLifecycleController : ApiControllerBase
{
    [HttpDelete]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    public async Task<IActionResult> Archive(
        [FromRoute] string projectId,
        [FromServices] ProjectService service,
        CancellationToken cancellationToken)
    {
        await service.ArchiveAsync(projectId, HttpContext.TraceIdentifier, cancellationToken);
        return OkEnvelopeResult(new { archived = true });
    }

    [HttpPost("restore")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    public async Task<IActionResult> Restore(
        [FromRoute] string projectId,
        [FromServices] ProjectService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RestoreAsync(
            projectId, HttpContext.TraceIdentifier, cancellationToken));
}
