using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Binding;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

namespace Zumbo.Api.Presentation.Controllers.Integrations;

[ApiController]
[Route("/api/integrations/webhooks")]
[Authorize]
[EnableRateLimiting("api")]
[Tags("Integrations")]
[ZumboPermission(PermissionCatalog.IntegrationManage)]
public sealed class WebhookSubscriptionsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromServices] ListWebhookSubscriptionsHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new ListWebhookSubscriptionsQuery(), cancellationToken));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get([FromRoute] string id, [FromServices] GetWebhookSubscriptionHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new GetWebhookSubscriptionQuery(id), cancellationToken));

    [HttpPost]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Create([FromBody] CreateWebhookSubscriptionRequest request, [FromServices] CreateSubscriptionHandler handler, CancellationToken cancellationToken) =>
        CreatedEnvelopeResult(await handler.HandleAsync(new CreateSubscriptionCommand(request, HttpContext.TraceIdentifier), cancellationToken));

    [HttpPut("{id}")]
    [Consumes("application/json")]
    [MinimalApiEmptyBadRequest]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] UpdateWebhookSubscriptionRequest request, [FromServices] UpdateSubscriptionHandler handler, CancellationToken cancellationToken) =>
        OkEnvelopeResult(await handler.HandleAsync(new UpdateSubscriptionCommand(id, request, HttpContext.TraceIdentifier), cancellationToken));
}
