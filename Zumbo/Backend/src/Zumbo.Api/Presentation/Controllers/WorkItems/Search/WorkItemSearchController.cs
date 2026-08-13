using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Search;

[ApiController]
[Route("/api/work-items")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemSearchController : ApiControllerBase
{
    [HttpGet]
    [EnableRateLimiting("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? projectId,
        [FromQuery] string? assigneeUserId,
        [FromQuery] string? status,
        [FromQuery] string? text,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool? archived,
        [FromQuery] string? issueType,
        [FromQuery] string? customFieldKey,
        [FromQuery] string? customFieldValue,
        [FromServices] SearchWorkItemsHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new WorkItemSearchRequest(
                projectId,
                assigneeUserId,
                status,
                text,
                page ?? 1,
                pageSize ?? 100,
                archived ?? false,
                issueType,
                customFieldKey,
                customFieldValue),
            cancellationToken));

    [HttpPost("search")]
    [Consumes("application/json")]
    [EnableRateLimiting("search")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SearchPage(
        [FromBody] WorkItemSearchRequest request,
        [FromServices] SearchWorkItemsHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandlePageAsync(request, cancellationToken));
}
