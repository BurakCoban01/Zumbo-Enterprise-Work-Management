using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

using static ApiEndpointResults;

internal static class WebhookEndpoints
{
    internal static void MapWebhookEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/integrations/webhooks")
            .WithTags("Integrations")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.IntegrationManage);

        group.MapPost("/", async (
            CreateWebhookSubscriptionRequest request,
            CreateSubscriptionHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Created(await handler.HandleAsync(
                new CreateSubscriptionCommand(request, http.TraceIdentifier),
                ct), http));

        group.MapGet("/", async (
            ListWebhookSubscriptionsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new ListWebhookSubscriptionsQuery(), ct), http));

        group.MapGet("/metrics", async (
            GetWebhookDeliveryMetricsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetWebhookDeliveryMetricsQuery(), ct), http));

        group.MapGet("/{id}", async (
            string id,
            GetWebhookSubscriptionHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetWebhookSubscriptionQuery(id), ct), http));

        group.MapPut("/{id}", async (
            string id,
            UpdateWebhookSubscriptionRequest request,
            UpdateSubscriptionHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new UpdateSubscriptionCommand(id, request, http.TraceIdentifier),
                ct), http));

        group.MapPost("/{id}/rotate-secret", async (
            string id,
            RotateWebhookSecretRequest request,
            RotateSecretHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new RotateSecretCommand(id, request, http.TraceIdentifier),
                ct), http))
            .RequireRateLimiting("bulk");

        group.MapPost("/{id}/enable", async (
            string id,
            SetWebhookSubscriptionStateRequest request,
            SetSubscriptionStateHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SetSubscriptionStateCommand(id, true, request, http.TraceIdentifier),
                ct), http));

        group.MapPost("/{id}/disable", async (
            string id,
            SetWebhookSubscriptionStateRequest request,
            SetSubscriptionStateHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new SetSubscriptionStateCommand(id, false, request, http.TraceIdentifier),
                ct), http));

        group.MapPost("/{id}/test-delivery", async (
            string id,
            QueueTestDeliveryHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Created(await handler.HandleAsync(
                new QueueTestDeliveryCommand(id, http.TraceIdentifier),
                ct), http))
            .RequireRateLimiting("bulk");

        group.MapGet("/{id}/deliveries", async (
            string id,
            string? cursor,
            int? pageSize,
            ListWebhookDeliveriesHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ListWebhookDeliveriesQuery(id, cursor, pageSize ?? 50),
                ct), http));

        group.MapGet("/deliveries/{deliveryId}", async (
            string deliveryId,
            GetWebhookDeliveryHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetWebhookDeliveryQuery(deliveryId), ct), http));

        group.MapPost("/deliveries/{deliveryId}/replay", async (
            string deliveryId,
            ReplayWebhookDeliveryHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ReplayWebhookDeliveryCommand(deliveryId, http.TraceIdentifier),
                ct), http))
            .RequireRateLimiting("bulk");
    }
}
