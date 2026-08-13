using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Schema;

[ApiController]
[Route("/api/work-items/{id}/custom-fields")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemCustomFieldsController : ApiControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Set(
        [FromRoute] string id,
        [FromBody] SetWorkItemCustomFieldsRequest request,
        [FromServices] SetCustomFieldsHandler handler,
        CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(
            new SetCustomFieldsCommand(id, request, HttpContext.TraceIdentifier),
            cancellationToken));
}
