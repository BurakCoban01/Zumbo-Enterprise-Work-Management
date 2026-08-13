using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Core;

[ApiController]
[Route("/api/work-items")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemCatalogController : ApiControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> Get([FromRoute] string id, [FromServices] GetWorkItemHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetWorkItemQuery(id), cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemCreate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] CreateWorkItemRequest request, [FromServices] CreateWorkItemHandler handler, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPut("{id}")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemUpdate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] UpdateWorkItemRequest request, [FromServices] UpdateWorkItemHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new UpdateWorkItemCommand(id, request, HttpContext.TraceIdentifier), cancellationToken));
}
