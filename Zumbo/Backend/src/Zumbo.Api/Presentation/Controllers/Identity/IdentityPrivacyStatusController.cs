using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth/privacy/jobs/{jobId}/status")]
[AllowAnonymous]
[EnableRateLimiting("api")]
[Tags("Identity")]
public sealed class IdentityPrivacyStatusController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromRoute] string jobId, [FromServices] PrivacyWorkflowService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.GetPublicStatusAsync(jobId, Request.Headers["X-Privacy-Status-Token"].ToString(), cancellationToken));

    [HttpPost("recover")]
    [EnableRateLimiting("password-reset")]
    public async Task<IActionResult> Recover([FromRoute] string jobId, [FromServices] PrivacyWorkflowService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.RecoverWithTokenAsync(jobId, Request.Headers["X-Privacy-Status-Token"].ToString(), cancellationToken));

    [HttpDelete]
    [EnableRateLimiting("password-reset")]
    public async Task<IActionResult> Purge([FromRoute] string jobId, [FromServices] PrivacyWorkflowService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.PurgeWithTokenAsync(jobId, Request.Headers["X-Privacy-Status-Token"].ToString(), cancellationToken));
}
