using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Development.Connections;
using Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;

namespace Zumbo.Api.Presentation.Controllers.Integrations;

[ApiController]
[Route("/api/integrations/development/{connectionId}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Development integrations")]
[ZumboPermission(PermissionCatalog.IntegrationManage)]
[DurableTransaction("WorkItems")]
public sealed class DevelopmentConnectionLifecycleController : ApiControllerBase
{
    [HttpPost("rotate-credential")]
    [Consumes("application/json")]
    [EnableRateLimiting("bulk")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> RotateCredential([FromRoute] string connectionId, [FromBody] RotateDevelopmentCredentialRequest request, [FromServices] RotateCredentialHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new RotateCredentialCommand(connectionId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("rotate-webhook-secret")]
    [Consumes("application/json")]
    [EnableRateLimiting("bulk")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> RotateWebhookSecret([FromRoute] string connectionId, [FromBody] DevelopmentVersionRequest request, [FromServices] RotateWebhookSecretHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new RotateWebhookSecretCommand(connectionId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("disconnect")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Disconnect([FromRoute] string connectionId, [FromBody] DevelopmentVersionRequest request, [FromServices] DisconnectConnectionHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new DisconnectConnectionCommand(connectionId, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpDelete]
    public async Task<IActionResult> Delete([FromRoute] string connectionId, [FromQuery, BindRequired] long expectedVersion, [FromServices] DeleteConnectionHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new DeleteConnectionCommand(connectionId, expectedVersion, HttpContext.TraceIdentifier), cancellationToken);
        return NoContent();
    }
}
