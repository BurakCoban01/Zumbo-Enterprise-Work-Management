using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

namespace Zumbo.Api.Presentation.Controllers.Integrations;

[ApiController]
[Route("/api/integrations/webhooks")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Integrations")]
[ZumboPermission(PermissionCatalog.IntegrationManage)]
public sealed class WebhookDeliveriesController : ApiControllerBase
{
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics([FromServices] GetWebhookDeliveryMetricsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetWebhookDeliveryMetricsQuery(), cancellationToken));

    [HttpGet("deliveries/{deliveryId}")]
    public async Task<IActionResult> Get([FromRoute] string deliveryId, [FromServices] GetWebhookDeliveryHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetWebhookDeliveryQuery(deliveryId), cancellationToken));

    [HttpPost("deliveries/{deliveryId}/replay")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> Replay([FromRoute] string deliveryId, [FromServices] ReplayWebhookDeliveryHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ReplayWebhookDeliveryCommand(deliveryId, HttpContext.TraceIdentifier), cancellationToken));
}
