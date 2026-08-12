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
[Route("/api/goals")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Goals")]
[ZumboPermission(PermissionCatalog.ProjectView)]
public sealed class GoalCatalogController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? includeArchived, [FromQuery] int? page, [FromQuery] int? pageSize, [FromServices] ListGoalsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListGoalsQuery(includeArchived ?? false, page ?? 1, pageSize ?? 50), cancellationToken));

    [HttpGet("{goalId}")]
    public async Task<IActionResult> Get([FromRoute] string goalId, [FromQuery] bool? includeArchived, [FromServices] GetGoalHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetGoalQuery(goalId, includeArchived ?? false), cancellationToken));

    [HttpGet("{goalId}/rollup")]
    public async Task<IActionResult> GetRollup([FromRoute] string goalId, [FromServices] GetGoalRollupHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetGoalRollupQuery(goalId), cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] SaveGoalRequest request, [FromServices] SaveGoalHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SaveGoalCommand(null, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPut("{goalId}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string goalId, [FromBody] SaveGoalRequest request, [FromServices] SaveGoalHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SaveGoalCommand(goalId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpDelete("{goalId}")]
    public async Task<IActionResult> Archive([FromRoute] string goalId, [FromServices] ArchiveGoalHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new ArchiveGoalCommand(goalId, HttpContext.TraceIdentifier), cancellationToken);
        return OkEnvelopeResult(new { archived = true });
    }
}
