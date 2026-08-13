using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
public sealed class WorkItemRecurrenceQueriesController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery, BindRequired] string projectId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? includeArchived,
        [FromServices] ListWorkItemRecurrencesHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new ListWorkItemRecurrencesQuery(projectId, page ?? 1, pageSize ?? 50, includeArchived ?? false),
            cancellationToken));

    [HttpPost("preview")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemCreate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Preview(
        [FromBody] PreviewWorkItemRecurrenceRequest request,
        [FromServices] PreviewWorkItemRecurrenceHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new PreviewWorkItemRecurrenceQuery(request), cancellationToken));

    [HttpGet("{recurrenceId}/occurrences")]
    public async Task<IActionResult> ListOccurrences(
        [FromRoute] string recurrenceId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] ListRecurrenceOccurrencesHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new ListRecurrenceOccurrencesQuery(recurrenceId, page ?? 1, pageSize ?? 50),
            cancellationToken));
}
