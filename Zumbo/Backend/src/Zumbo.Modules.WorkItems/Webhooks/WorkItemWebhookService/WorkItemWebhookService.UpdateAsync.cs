using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<WebhookSubscriptionResponse> UpdateAsync(
        string id,
        UpdateWebhookSubscriptionRequest request,
        CancellationToken ct,
        string? correlationId = null)
    {
        var document = await FindOwnedAsync(id, ct);
        var oldValue = SubscriptionAuditValue(document);
        var targetUrl = RequireTarget(request.TargetUrl);
        await targetPolicy.ValidateAsync(targetUrl, ct);
        document.Name = RequireName(request.Name);
        document.TargetUrl = targetUrl;
        document.EventScopes = NormalizeScopes(request.EventScopes);
        document.UpdatedAtUtc = clock.UtcNow;
        var updated = await ReplaceAsync(document, request.ExpectedVersion, ct);
        await WriteAuditAsync(
            "WebhookSubscriptionUpdated",
            "WebhookSubscription",
            updated.Id,
            oldValue,
            SubscriptionAuditValue(updated),
            correlationId,
            ct);
        return ToResponse(updated);
    }
}
