using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;

public sealed class QueueDeliveryHandler(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IDocumentRepository<WebhookDeliveryDocument> deliveries,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web);

    public async Task HandleAsync(
        QueueDeliveryCommand command,
        CancellationToken ct)
    {
        var scope = WorkItemWebhookScopes.FromEventType(command.Message.EventType);
        if (!WorkItemWebhookScopes.All.Contains(scope))
        {
            return;
        }

        var candidates = await ListActiveSubscriptionsAsync(
            command.OrganizationId,
            ct);
        foreach (var subscription in candidates.Where(
                     item => item.EventScopes.Contains(scope, StringComparer.Ordinal)))
        {
            var id = Hash($"{subscription.Id}:{command.SourceEventId}");
            var payload = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                specVersion = "1.0",
                id,
                type = scope,
                source = "zumbo/work-items",
                subject = $"work-items/{command.Message.WorkItemId}",
                time = command.Message.OccurredAtUtc,
                tenantId = command.OrganizationId,
                correlationId = command.Message.CorrelationId,
                data = new
                {
                    command.Message.WorkItemId,
                    command.Message.ProjectId,
                    command.Message.BoardId,
                    command.Message.WorkItem,
                    command.Message.ResourceVersion
                }
            }, JsonOptions);
            try
            {
                var now = clock.UtcNow;
                await deliveries.CreateAsync(new WebhookDeliveryDocument
                {
                    Id = id,
                    OrganizationId = command.OrganizationId,
                    SubscriptionId = subscription.Id,
                    SourceEventId = command.SourceEventId,
                    EventScope = scope,
                    TargetUrl = subscription.TargetUrl,
                    Payload = payload,
                    PayloadSha256 = Hash(payload),
                    NextAttemptAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }, ct);
            }
            catch (DocumentConflictException)
            {
                // Durable replays retain idempotency through the deterministic ID.
            }
        }
    }

    private async Task<IReadOnlyList<WebhookSubscriptionDocument>>
        ListActiveSubscriptionsAsync(
            string organizationId,
            CancellationToken ct)
    {
        var result = new List<WebhookSubscriptionDocument>();
        string? cursor = null;
        do
        {
            var page = await subscriptions.ListByCursorAsync(
                item => item.OrganizationId == organizationId && item.IsActive,
                cursor,
                200,
                ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return result;
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
