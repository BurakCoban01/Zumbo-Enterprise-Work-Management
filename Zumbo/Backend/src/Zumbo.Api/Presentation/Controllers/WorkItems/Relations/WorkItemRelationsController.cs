using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Relations;

[ApiController]
[Route("/api/work-items/{id}/relations")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemRelationsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemLink)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Link([FromRoute] string id, [FromBody] LinkWorkItemRequest request, [FromServices] LinkWorkItemHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new LinkWorkItemCommand(id, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpDelete("{relatedWorkItemId}")]
    [ZumboPermission(PermissionCatalog.WorkItemLink)]
    public async Task<IActionResult> Unlink([FromRoute] string id, [FromRoute] string relatedWorkItemId, [FromQuery, BindRequired] string relationType, [FromServices] UnlinkWorkItemHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new UnlinkWorkItemCommand(id, relatedWorkItemId, relationType, HttpContext.TraceIdentifier), cancellationToken));
}
