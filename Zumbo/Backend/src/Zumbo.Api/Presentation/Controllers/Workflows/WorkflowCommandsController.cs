using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Workflows;

namespace Zumbo.Api.Presentation.Controllers.Workflows;

[ApiController]
[Route("/api/workflows/{projectId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Workflows")]
[ZumboPermission(PermissionCatalog.WorkflowManage)]
public sealed class WorkflowCommandsController : ApiControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Upsert([FromRoute] string projectId, [FromBody] CreateWorkflowRequest request, [FromServices] UpsertWorkflowHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request with { ProjectId = projectId }, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPut("draft")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SaveDraft([FromRoute] string projectId, [FromBody] CreateWorkflowRequest request, [FromServices] SaveWorkflowDraftHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(request with { ProjectId = projectId }, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("publish")]
    public async Task<IActionResult> Publish([FromRoute] string projectId, [FromServices] PublishWorkflowHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(projectId, HttpContext.TraceIdentifier, cancellationToken));
}
