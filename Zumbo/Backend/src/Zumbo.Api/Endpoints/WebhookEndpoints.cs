using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

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
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.CreateAsync(request, ct, http.TraceIdentifier), http));

        group.MapGet("/", async (
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListAsync(ct), http));

        group.MapGet("/metrics", async (
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetMetricsAsync(ct), http));

        group.MapGet("/{id}", async (
            string id,
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(id, ct), http));

        group.MapPut("/{id}", async (
            string id,
            UpdateWebhookSubscriptionRequest request,
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.UpdateAsync(id, request, ct, http.TraceIdentifier), http));

        group.MapPost("/{id}/rotate-secret", async (
            string id,
            RotateWebhookSecretRequest request,
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.RotateSecretAsync(id, request, ct, http.TraceIdentifier), http))
            .RequireRateLimiting("bulk");

        group.MapPost("/{id}/enable", async (
            string id,
            SetWebhookSubscriptionStateRequest request,
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetActiveAsync(id, true, request, ct, http.TraceIdentifier), http));

        group.MapPost("/{id}/disable", async (
            string id,
            SetWebhookSubscriptionStateRequest request,
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetActiveAsync(id, false, request, ct, http.TraceIdentifier), http));

        group.MapPost("/{id}/test-delivery", async (
            string id,
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.QueueTestDeliveryAsync(id, ct, http.TraceIdentifier), http))
            .RequireRateLimiting("bulk");

        group.MapGet("/{id}/deliveries", async (
            string id,
            string? cursor,
            int? pageSize,
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListDeliveriesAsync(id, cursor, pageSize ?? 50, ct), http));

        group.MapGet("/deliveries/{deliveryId}", async (
            string deliveryId,
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetDeliveryAsync(deliveryId, ct), http));

        group.MapPost("/deliveries/{deliveryId}/replay", async (
            string deliveryId,
            WorkItemWebhookService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ReplayAsync(deliveryId, ct, http.TraceIdentifier), http))
            .RequireRateLimiting("bulk");
    }
}
