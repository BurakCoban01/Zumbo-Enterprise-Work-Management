using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Recurrences;

[ApiController]
[Route("/api/work-items/recurrences")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemRecurrenceLifecycleController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemCreate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create(
        [FromBody] CreateWorkItemRecurrenceRequest request,
        [FromServices] CreateWorkItemRecurrenceHandler handler,
        CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(
            new CreateWorkItemRecurrenceCommand(request, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpPatch("{recurrenceId}/state")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SetState(
        [FromRoute] string recurrenceId,
        [FromBody] SetWorkItemRecurrenceStateRequest request,
        [FromServices] SetWorkItemRecurrenceStateHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new SetWorkItemRecurrenceStateCommand(recurrenceId, request.Active, HttpContext.TraceIdentifier),
            cancellationToken));

    [HttpDelete("{recurrenceId}")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    public async Task<IActionResult> Archive(
        [FromRoute] string recurrenceId,
        [FromServices] ArchiveWorkItemRecurrenceHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(
            new ArchiveWorkItemRecurrenceCommand(recurrenceId, HttpContext.TraceIdentifier),
            cancellationToken);
        return NoContent();
    }

    [HttpPost("process-due")]
    [ZumboPermission(PermissionCatalog.OperationsManage, isGlobal: true)]
    public async Task<IActionResult> ProcessDue(
        [FromServices] ScheduleDueRecurrencesHandler handler,
        CancellationToken cancellationToken) =>
        Ok(new { scheduled = await handler.HandleAsync(new ScheduleDueRecurrencesCommand(), cancellationToken) });
}
