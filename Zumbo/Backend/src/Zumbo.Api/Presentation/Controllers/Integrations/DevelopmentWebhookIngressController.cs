using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Binding;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Presentation.Controllers.Integrations;

[ApiController]
[Route("/api/integrations/development/{connectionId}/webhook")]
[AllowAnonymous]
[EnableRateLimiting("api")]
[Tags("Development integrations")]
[DurableTransaction("WorkItems")]
public sealed class DevelopmentWebhookIngressController : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive([FromRoute] string connectionId, [FromServices] ReceiveWebhookHandler handler, CancellationToken cancellationToken)
    {
        var request = await DevelopmentWebhookRequestReader.ReadAsync(Request, cancellationToken);
        var result = await handler.HandleAsync(new ReceiveWebhookCommand(connectionId, request), cancellationToken);
        return StatusCode(
            StatusCodes.Status202Accepted,
            ApiResponse<DevelopmentWebhookResult>.Ok(result, HttpContext.TraceIdentifier));
    }
}
