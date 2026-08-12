using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;

namespace Zumbo.Api.Presentation.Controllers.Projects;

[ApiController]
[Route("/api/projects/{projectId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Projects")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class ProjectReleasesController : ApiControllerBase
{
    [HttpPost("versions")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> CreateVersion([FromRoute] string projectId, [FromBody] CreateProjectVersionRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.CreateVersionAsync(projectId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("versions/{versionId}")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    public async Task<IActionResult> ArchiveVersion([FromRoute] string projectId, [FromRoute] string versionId, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ArchiveVersionAsync(projectId, versionId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("releases")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> CreateRelease([FromRoute] string projectId, [FromBody] CreateProjectReleaseRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.CreateReleaseAsync(projectId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("releases/{releaseId}/approve")]
    [ZumboPermission(PermissionCatalog.ReleaseApprove)]
    public async Task<IActionResult> ApproveRelease([FromRoute] string projectId, [FromRoute] string releaseId, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ApproveReleaseAsync(projectId, releaseId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("releases/{releaseId}/publish")]
    [ZumboPermission(PermissionCatalog.ReleasePublish)]
    public async Task<IActionResult> PublishRelease([FromRoute] string projectId, [FromRoute] string releaseId, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.PublishReleaseAsync(projectId, releaseId, HttpContext.TraceIdentifier, cancellationToken));
}
