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
[Route("/api/goals/{goalId}/key-results")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Goals")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class GoalKeyResultsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromRoute] string goalId, [FromBody] SaveKeyResultRequest request, [FromServices] SaveKeyResultHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SaveKeyResultCommand(goalId, null, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPut("{keyResultId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string goalId, [FromRoute] string keyResultId, [FromBody] SaveKeyResultRequest request, [FromServices] SaveKeyResultHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SaveKeyResultCommand(goalId, keyResultId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("{keyResultId}/progress-updates")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> AddProgress([FromRoute] string goalId, [FromRoute] string keyResultId, [FromBody] AddKeyResultProgressRequest request, [FromServices] AddKeyResultProgressHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new AddKeyResultProgressCommand(goalId, keyResultId, request, HttpContext.TraceIdentifier), cancellationToken));
}
