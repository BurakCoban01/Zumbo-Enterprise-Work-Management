using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Projects.Application.Features.Goals;

namespace Zumbo.Api.Presentation.Controllers.Strategy;

[ApiController]
[Route("/api/goals/{goalId}/status-updates")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Goals")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class GoalStatusController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Add([FromRoute] string goalId, [FromBody] AddGoalStatusUpdateRequest request, [FromServices] AddGoalStatusUpdateHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new AddGoalStatusUpdateCommand(goalId, request, HttpContext.TraceIdentifier), cancellationToken));
}
