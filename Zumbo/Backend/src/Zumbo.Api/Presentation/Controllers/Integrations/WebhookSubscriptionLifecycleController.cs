using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

namespace Zumbo.Api.Presentation.Controllers.Integrations;

[ApiController]
[Route("/api/integrations/webhooks/{id}")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Integrations")]
[ZumboPermission(PermissionCatalog.IntegrationManage)]
public sealed class WebhookSubscriptionLifecycleController : ApiControllerBase
{
    [HttpPost("rotate-secret")]
    [Consumes("application/json")]
    [EnableRateLimiting("bulk")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> RotateSecret([FromRoute] string id, [FromBody] RotateWebhookSecretRequest request, [FromServices] RotateSecretHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new RotateSecretCommand(id, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("enable")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Enable([FromRoute] string id, [FromBody] SetWebhookSubscriptionStateRequest request, [FromServices] SetSubscriptionStateHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SetSubscriptionStateCommand(id, true, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("disable")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Disable([FromRoute] string id, [FromBody] SetWebhookSubscriptionStateRequest request, [FromServices] SetSubscriptionStateHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new SetSubscriptionStateCommand(id, false, request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPost("test-delivery")]
    [EnableRateLimiting("bulk")]
    public async Task<IActionResult> QueueTestDelivery([FromRoute] string id, [FromServices] QueueTestDeliveryHandler handler, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(new QueueTestDeliveryCommand(id, HttpContext.TraceIdentifier), cancellationToken));

    [HttpGet("deliveries")]
    public async Task<IActionResult> ListDeliveries([FromRoute] string id, [FromQuery] string? cursor, [FromQuery] int? pageSize, [FromServices] ListWebhookDeliveriesHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListWebhookDeliveriesQuery(id, cursor, pageSize ?? 50), cancellationToken));
}
