using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<WebhookSubscriptionResponse> SetActiveAsync(
        string id,
        bool active,
        SetWebhookSubscriptionStateRequest request,
        CancellationToken ct,
        string? correlationId = null)
    {
        var document = await FindOwnedAsync(id, ct);
        var wasActive = document.IsActive;
        document.IsActive = active;
        document.UpdatedAtUtc = clock.UtcNow;
        var updated = await ReplaceAsync(document, request.ExpectedVersion, ct);
        await WriteAuditAsync(
            active ? "WebhookSubscriptionEnabled" : "WebhookSubscriptionDisabled",
            "WebhookSubscription",
            updated.Id,
            wasActive.ToString(),
            active.ToString(),
            correlationId,
            ct);
        return ToResponse(updated);
    }
}
