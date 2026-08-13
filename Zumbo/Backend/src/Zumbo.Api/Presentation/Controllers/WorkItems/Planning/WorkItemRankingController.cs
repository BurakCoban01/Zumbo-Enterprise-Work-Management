using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Planning;

[ApiController]
[Route("/api/work-items/{id}/rank")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemMove)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemRankingController : ApiControllerBase
{
    [HttpPatch]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Reorder(
        [FromRoute] string id,
        [FromBody] ReorderWorkItemRequest request,
        [FromServices] ReorderWorkItemHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new ReorderWorkItemCommand(id, request, HttpContext.TraceIdentifier),
            cancellationToken));
}
