using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Approvals;

[ApiController]
[Route("/api/work-items/{id}/approvals")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemApprovalsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromRoute] string id, [FromQuery] int? page, [FromQuery] int? pageSize, [FromServices] WorkItemActivityQueryService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListApprovalsAsync(id, page ?? 1, pageSize ?? 50, cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemApprove)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> RequestApproval([FromRoute] string id, [FromBody] RequestWorkItemApprovalRequest request, [FromServices] RequestApprovalHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new RequestApprovalCommand(id, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("{approvalId}/decision")]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkItemApprove)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Decide([FromRoute] string id, [FromRoute] string approvalId, [FromBody] DecideWorkItemApprovalRequest request, [FromServices] DecideApprovalHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new DecideApprovalCommand(id, approvalId, request, HttpContext.TraceIdentifier), cancellationToken));
}
