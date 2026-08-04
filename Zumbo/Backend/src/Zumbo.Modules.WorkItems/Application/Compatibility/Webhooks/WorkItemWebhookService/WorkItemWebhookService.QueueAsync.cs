using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task QueueAsync(
        string sourceEventId,
        string organizationId,
        WorkItemWebhookEvent message,
        CancellationToken ct)
    {
        var scope = WorkItemWebhookScopes.FromEventType(message.EventType);
        if (!WorkItemWebhookScopes.All.Contains(scope)) return;
        var candidates = await ListActiveSubscriptionsAsync(organizationId, ct);
        foreach (var subscription in candidates.Where(x => x.EventScopes.Contains(scope, StringComparer.Ordinal)))
        {
            var id = Hash($"{subscription.Id}:{sourceEventId}");
            var payload = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                specVersion = "1.0",
                id,
                type = scope,
                source = "zumbo/work-items",
                subject = $"work-items/{message.WorkItemId}",
                time = message.OccurredAtUtc,
                tenantId = organizationId,
                correlationId = message.CorrelationId,
                data = new
                {
                    message.WorkItemId,
                    message.ProjectId,
                    message.BoardId,
                    message.WorkItem,
                    message.ResourceVersion
                }
            }, JsonOptions);
            try
            {
                var now = clock.UtcNow;
                await deliveries.CreateAsync(new WebhookDeliveryDocument
                {
                    Id = id,
                    OrganizationId = organizationId,
                    SubscriptionId = subscription.Id,
                    SourceEventId = sourceEventId,
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
                // The durable consumer may replay; the deterministic delivery id makes queueing idempotent.
            }
        }
    }
}
