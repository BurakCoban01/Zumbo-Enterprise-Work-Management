using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Contracts.Automations;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Workflows;

namespace Zumbo.Api.Presentation.Controllers.Automations;

[ApiController]
[Route("/api/automations")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Automations")]
[ZumboPermission(PermissionCatalog.WorkflowManage)]
[DurableTransaction("Workflows")]
public sealed class AutomationCommandsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] DefineAutomationRuleRequest request, [FromServices] AutomationRuleService service, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await service.SaveDraftAsync(null, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPut("{ruleId}/draft")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SaveDraft([FromRoute] string ruleId, [FromBody] DefineAutomationRuleRequest request, [FromServices] AutomationRuleService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.SaveDraftAsync(ruleId, request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("{ruleId}/publish")]
    public async Task<IActionResult> Publish([FromRoute] string ruleId, [FromServices] AutomationRuleService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.PublishAsync(ruleId, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPatch("{ruleId}/state")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SetState([FromRoute] string ruleId, [FromBody] SetAutomationStateRequest request, [FromServices] AutomationRuleService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.SetActiveAsync(ruleId, request.Active, HttpContext.TraceIdentifier, cancellationToken));

    [HttpDelete("{ruleId}")]
    public async Task<IActionResult> Archive([FromRoute] string ruleId, [FromServices] AutomationRuleService service, CancellationToken cancellationToken)
    {
        await service.ArchiveAsync(ruleId, HttpContext.TraceIdentifier, cancellationToken);
        return NoContent();
    }

    [HttpPost("{ruleId}/dry-run")]
    [Consumes("application/json")]
    [EnableRateLimiting("report")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> DryRun([FromRoute] string ruleId, [FromBody] AutomationDryRunContext context, [FromServices] AutomationRuleService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.DryRunAsync(ruleId, context, cancellationToken));
}
