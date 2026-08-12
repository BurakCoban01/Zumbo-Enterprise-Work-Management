using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;

namespace Zumbo.Api.Presentation.Controllers.Projects;

[ApiController]
[Route("/api/projects/{projectId}/components")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Projects")]
[ZumboPermission(PermissionCatalog.ProjectManage)]
public sealed class ProjectComponentsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromRoute] string projectId, [FromBody] CreateProjectComponentRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.CreateComponentAsync(projectId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPut("{componentId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string projectId, [FromRoute] string componentId, [FromBody] UpdateProjectComponentRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpdateComponentAsync(projectId, componentId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("{componentId}")]
    public async Task<IActionResult> Archive([FromRoute] string projectId, [FromRoute] string componentId, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ArchiveComponentAsync(projectId, componentId, HttpContext.TraceIdentifier, cancellationToken));
}
