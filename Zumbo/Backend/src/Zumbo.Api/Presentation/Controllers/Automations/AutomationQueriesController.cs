using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.Workflows.Application.Features.RunQueries;

namespace Zumbo.Api.Presentation.Controllers.Automations;

[ApiController]
[Route("/api/automations")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Automations")]
[ZumboPermission(PermissionCatalog.WorkflowView)]
public sealed class AutomationQueriesController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery, BindRequired] string projectId, [FromQuery] bool? includeArchived, [FromQuery] int? page, [FromQuery] int? pageSize, [FromServices] AutomationRuleService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListAsync(projectId, includeArchived ?? false, page ?? 1, pageSize ?? 50, cancellationToken));

    [HttpGet("{ruleId}")]
    public async Task<IActionResult> Get([FromRoute] string ruleId, [FromQuery] bool? draft, [FromServices] AutomationRuleService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetAsync(ruleId, draft ?? false, cancellationToken));

    [HttpGet("runs")]
    public async Task<IActionResult> ListRuns([FromQuery, BindRequired] string projectId, [FromQuery] string? ruleId, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, [FromServices] ListAutomationRunsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListAutomationRunsQuery(projectId, ruleId, status, page ?? 1, pageSize ?? 50), cancellationToken));

    [HttpGet("runs/{runId}")]
    public async Task<IActionResult> GetRun([FromRoute] string runId, [FromServices] GetAutomationRunHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetAutomationRunQuery(runId), cancellationToken));
}
