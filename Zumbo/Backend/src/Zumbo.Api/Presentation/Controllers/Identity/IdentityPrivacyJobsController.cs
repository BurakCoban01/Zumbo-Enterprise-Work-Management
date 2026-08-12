using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth/privacy/jobs")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Identity")]
[ZumboPermission(PermissionCatalog.ProfileRead)]
public sealed class IdentityPrivacyJobsController : ApiControllerBase
{
    [HttpGet("{jobId}")]
    public async Task<IActionResult> Get([FromRoute] string jobId, [FromServices] PrivacyWorkflowService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetAsync(jobId, cancellationToken));

    [HttpPost("{jobId}/retry")]
    [EnableRateLimiting("password-reset")]
    public async Task<IActionResult> Retry([FromRoute] string jobId, [FromServices] PrivacyWorkflowService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RetryAsync(jobId, cancellationToken));

    [HttpPost("{jobId}/reconcile")]
    [EnableRateLimiting("password-reset")]
    public async Task<IActionResult> Reconcile([FromRoute] string jobId, [FromServices] PrivacyWorkflowService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.ReconcileAsync(jobId, cancellationToken));

    [HttpPost("retention/purge")]
    public async Task<IActionResult> PurgeExpired([FromServices] PrivacyWorkflowService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.PurgeExpiredAsync(cancellationToken));
}
