using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Workflows.Application.Features.RunReplay;

namespace Zumbo.Api.Presentation.Controllers.Automations;

[ApiController]
[Route("/api/automations/runs")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Automations")]
[ZumboPermission(PermissionCatalog.WorkflowManage)]
[DurableTransaction("Workflows")]
public sealed class AutomationRunCommandsController : ApiControllerBase
{
    [HttpPost("{runId}/replay")]
    public async Task<IActionResult> Replay([FromRoute] string runId, [FromServices] ReplayAutomationRunHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ReplayAutomationRunCommand(runId, HttpContext.TraceIdentifier), cancellationToken));
}
