using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Api.Presentation.Controllers.WorkItems.BulkOperations;

[ApiController]
[Route("/api/work-items/bulk/jobs/{jobId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("WorkItems")]
[ZumboPermission(PermissionCatalog.WorkItemView)]
[DurableTransaction("WorkItems")]
public sealed class WorkItemBulkArtifactsController : ApiControllerBase
{
    [HttpGet("result")]
    public async Task<IActionResult> GetResult([FromRoute] string jobId, [FromServices] WorkItemBulkJobService service, CancellationToken cancellationToken)
    {
        var file = await service.OpenArtifactAsync(jobId, errors: false, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
    }

    [HttpGet("errors")]
    public async Task<IActionResult> GetErrors([FromRoute] string jobId, [FromServices] WorkItemBulkJobService service, CancellationToken cancellationToken)
    {
        var file = await service.OpenArtifactAsync(jobId, errors: true, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
    }
}
