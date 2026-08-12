using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;

namespace Zumbo.Api.Presentation.Controllers.Projects;

[ApiController]
[Route("/api/projects/{projectId}/milestones")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Projects")]
[ZumboPermission(PermissionCatalog.ProjectManage)]
public sealed class ProjectMilestonesController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromRoute] string projectId, [FromBody] CreateProjectMilestoneRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.CreateMilestoneAsync(projectId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPut("{milestoneId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string projectId, [FromRoute] string milestoneId, [FromBody] UpdateProjectMilestoneRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpdateMilestoneAsync(projectId, milestoneId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("{milestoneId}/complete")]
    public async Task<IActionResult> Complete([FromRoute] string projectId, [FromRoute] string milestoneId, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.CompleteMilestoneAsync(projectId, milestoneId, HttpContext.TraceIdentifier, cancellationToken));
}
