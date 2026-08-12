using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.Worklogs;

[ApiController]
[Route("/api/work-items/{id}/worklogs")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemWorklogsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromRoute] string id, [FromQuery] int? page, [FromQuery] int? pageSize, [FromServices] WorkItemActivityQueryService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ListWorkLogsAsync(id, page ?? 1, pageSize ?? 50, cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [ZumboPermission(PermissionCatalog.WorkLogCreate)]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Add([FromRoute] string id, [FromBody] AddWorkLogRequest request, [FromServices] AddWorkLogHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new AddWorkLogCommand(id, request), cancellationToken));
}
