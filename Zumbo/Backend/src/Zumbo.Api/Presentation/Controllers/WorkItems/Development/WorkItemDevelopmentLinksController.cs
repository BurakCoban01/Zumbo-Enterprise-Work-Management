using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Development.Links;
using Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Development;

[ApiController]
[Route("/api/work-items/{workItemId}/development-links")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Development integrations")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemDevelopmentLinksController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromRoute] string workItemId, [FromServices] ListWorkItemLinksHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListWorkItemLinksQuery(workItemId), cancellationToken));

    [HttpGet("mappings")]
    [ZumboPermission(PermissionCatalog.WorkItemLink)]
    public async Task<IActionResult> ListMappings([FromRoute] string workItemId, [FromServices] ListWorkItemMappingsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListWorkItemMappingsQuery(workItemId), cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemLink)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromRoute] string workItemId, [FromBody] CreateWorkItemDevelopmentLinkRequest request, [FromServices] CreateWorkItemLinkHandler handler, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(new CreateWorkItemLinkCommand(workItemId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpDelete("{linkId}")]
    [ZumboPermission(PermissionCatalog.WorkItemLink)]
    public async Task<IActionResult> Delete([FromRoute] string workItemId, [FromRoute] string linkId, [FromQuery, BindRequired] long expectedVersion, [FromServices] DeleteWorkItemLinkHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new DeleteWorkItemLinkCommand(workItemId, linkId, expectedVersion, HttpContext.TraceIdentifier), cancellationToken);
        return NoContent();
    }
}
