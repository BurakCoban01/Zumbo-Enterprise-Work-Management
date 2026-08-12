using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;

namespace Zumbo.Api.Presentation.Controllers.Projects;

[ApiController]
[Route("/api/projects")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Projects")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class ProjectCatalogController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [MinimalApiRequiredQuery] string organizationId,
        [FromQuery] bool? archived,
        [FromServices] ListProjectsHandler handler,
        CancellationToken cancellationToken)
    {
        if (!Request.Query.ContainsKey(nameof(organizationId)))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new EmptyResult();
        }

        return OkEnvelopeResult(await handler.HandleAsync(
            new ListProjectsQuery(organizationId, archived ?? false), cancellationToken));
    }

    [HttpGet("{projectId}")]
    public async Task<IActionResult> Get(
        [FromRoute] string projectId,
        [FromServices] ProjectService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetAsync(projectId, cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create(
        [FromBody] CreateProjectRequest request,
        [FromServices] CreateProjectHandler handler,
        CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(
            request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPut("{projectId}")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.ProjectManage)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update(
        [FromRoute] string projectId,
        [FromBody] UpdateProjectRequest request,
        [FromServices] ProjectService service,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.UpdateAsync(
            projectId, request, HttpContext.TraceIdentifier, cancellationToken));
}
