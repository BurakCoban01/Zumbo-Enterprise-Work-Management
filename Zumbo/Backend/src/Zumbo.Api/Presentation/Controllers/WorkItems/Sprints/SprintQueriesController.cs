using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Sprints;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Sprints;

[ApiController]
[Route("/api/sprints")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Sprints")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class SprintQueriesController : ApiControllerBase
{
    [HttpGet("{sprintId}")]
    public async Task<IActionResult> Get([FromRoute] string sprintId, [FromServices] GetSprintHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetSprintQuery(sprintId), cancellationToken));

    [HttpGet("projects/{projectId}")]
    public async Task<IActionResult> List([FromRoute] string projectId, [FromQuery] string? after, [FromQuery] int? pageSize, [FromServices] ListSprintsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListSprintsQuery(projectId, after, pageSize ?? 50), cancellationToken));

    [HttpGet("projects/{projectId}/backlog")]
    public async Task<IActionResult> Backlog([FromRoute] string projectId, [FromQuery] string? after, [FromQuery] int? pageSize, [FromServices] ListSprintBacklogHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListSprintBacklogQuery(projectId, after, pageSize ?? 50), cancellationToken));

    [HttpGet("{sprintId}/burndown")]
    public async Task<IActionResult> Burndown([FromRoute] string sprintId, [FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, [FromServices] GetSprintHandler getSprint, [FromServices] GetSprintBurndownHandler burndown, CancellationToken cancellationToken)
    {
        var sprint = await getSprint.HandleAsync(new GetSprintQuery(sprintId), cancellationToken);
        var snapshot = await burndown.HandleAsync(new GetSprintBurndownQuery(sprint.ProjectId, sprintId, startDate, endDate), cancellationToken);
        return OkEnvelopeResult(snapshot.Data);
    }

    [HttpGet("projects/{projectId}/velocity")]
    public async Task<IActionResult> Velocity([FromRoute] string projectId, [FromQuery] int? sprintCount, [FromServices] GetSprintVelocityHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult((await handler.HandleAsync(new GetSprintVelocityQuery(projectId, sprintCount ?? 6), cancellationToken)).Data);
}
