using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;

namespace Zumbo.Api.Presentation.Controllers.Projects;

[ApiController]
[Route("/api/projects/{projectId}/templates")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Projects")]
[ZumboPermission(PermissionCatalog.ProjectManage)]
public sealed class ProjectTemplatesController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromRoute] string projectId, [FromBody] UpsertProjectTemplateRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpsertTemplateAsync(projectId, null, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPut("{templateId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string projectId, [FromRoute] string templateId, [FromBody] UpsertProjectTemplateRequest request, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpsertTemplateAsync(projectId, templateId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("{templateId}")]
    public async Task<IActionResult> Archive([FromRoute] string projectId, [FromRoute] string templateId, [FromServices] ProjectService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ArchiveTemplateAsync(projectId, templateId, HttpContext.TraceIdentifier, cancellationToken));
}
