using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Workflows;

namespace Zumbo.Api.Presentation.Controllers.Workflows;

[ApiController]
[Route("/api/workflows/{projectId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Workflows")]
[ZumboPermission(PermissionCatalog.WorkflowView)]
public sealed class WorkflowQueriesController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromRoute] string projectId, [FromServices] GetWorkflowHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetWorkflowQuery(projectId), cancellationToken));

    [HttpGet("draft")]
    public async Task<IActionResult> GetDraft([FromRoute] string projectId, [FromServices] WorkflowService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetDraftAsync(projectId, cancellationToken));

    [HttpGet("versions")]
    public async Task<IActionResult> ListVersions([FromRoute] string projectId, [FromServices] WorkflowService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListVersionsAsync(projectId, cancellationToken));
}
