using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<WebhookSecretReceipt> CreateAsync(
        CreateWebhookSubscriptionRequest request,
        CancellationToken ct,
        string? correlationId = null)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var rawSecret = GenerateSecret();
        var now = clock.UtcNow;
        var targetUrl = RequireTarget(request.TargetUrl);
        await targetPolicy.ValidateAsync(targetUrl, ct);
        var document = await subscriptions.CreateAsync(new WebhookSubscriptionDocument
        {
            OrganizationId = organizationId,
            Name = RequireName(request.Name),
            TargetUrl = targetUrl,
            EventScopes = NormalizeScopes(request.EventScopes),
            CurrentSecretProtected = secretProtector.Protect(rawSecret),
            CurrentSecretFingerprint = Fingerprint(rawSecret),
            CreatedByUserId = RequireUser(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        await WriteAuditAsync(
            "WebhookSubscriptionCreated",
            "WebhookSubscription",
            document.Id,
            null,
            SubscriptionAuditValue(document),
            correlationId,
            ct);
        return new WebhookSecretReceipt(ToResponse(document), rawSecret);
    }
}
