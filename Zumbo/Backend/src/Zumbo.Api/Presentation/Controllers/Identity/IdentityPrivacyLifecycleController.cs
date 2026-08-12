using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Presentation.Controllers.Identity;

[ApiController]
[Route("/api/auth/privacy")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Identity")]
[ZumboPermission(PermissionCatalog.ProfileRead)]
public sealed class IdentityPrivacyLifecycleController : ApiControllerBase
{
    [HttpPost("anonymize")]
    [Consumes("application/json")]
    [EnableRateLimiting("password-reset")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Anonymize([FromBody] AnonymizeAccountRequest request, [FromServices] PrivacyService service, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await service.AnonymizeAsync(request, HttpContext.TraceIdentifier, cancellationToken));

    [HttpPost("anonymization-jobs")]
    [Consumes("application/json")]
    [EnableRateLimiting("password-reset")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> SubmitAnonymization([FromBody] AnonymizeAccountRequest request, [FromServices] PrivacyWorkflowService service, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await service.SubmitAnonymizationAsync(request, cancellationToken));
}
