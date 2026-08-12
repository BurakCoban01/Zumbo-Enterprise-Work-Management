using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Labels;

[ApiController]
[Route("/api/work-items/{id}/labels")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemLabelsController : ApiControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Add([FromRoute] string id, [FromBody] AddLabelRequest request, [FromServices] AddLabelHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new AddLabelCommand(id, request), cancellationToken));

    [HttpDelete("{label}")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    public async Task<IActionResult> Remove([FromRoute] string id, [FromRoute] string label, [FromServices] RemoveLabelHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new RemoveLabelCommand(id, label), cancellationToken));
}
