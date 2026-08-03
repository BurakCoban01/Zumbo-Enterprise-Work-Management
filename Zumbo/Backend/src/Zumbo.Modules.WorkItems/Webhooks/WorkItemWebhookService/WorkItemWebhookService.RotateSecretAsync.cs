using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<WebhookSecretReceipt> RotateSecretAsync(
        string id,
        RotateWebhookSecretRequest request,
        CancellationToken ct,
        string? correlationId = null)
    {
        var document = await FindOwnedAsync(id, ct);
        var rawSecret = GenerateSecret();
        document.PreviousSecretProtected = document.CurrentSecretProtected;
        document.PreviousSecretFingerprint = document.CurrentSecretFingerprint;
        document.PreviousSecretVersion = document.SecretVersion;
        document.PreviousSecretValidUntilUtc = clock.UtcNow.AddMinutes(
            Math.Clamp(options.Value.RotationOverlapMinutes, 1, 1440));
        document.CurrentSecretProtected = secretProtector.Protect(rawSecret);
        document.CurrentSecretFingerprint = Fingerprint(rawSecret);
        document.SecretVersion++;
        document.UpdatedAtUtc = clock.UtcNow;
        var updated = await ReplaceAsync(document, request.ExpectedVersion, ct);
        await WriteAuditAsync(
            "WebhookSecretRotated",
            "WebhookSubscription",
            updated.Id,
            document.PreviousSecretFingerprint,
            updated.CurrentSecretFingerprint,
            correlationId,
            ct);
        return new WebhookSecretReceipt(ToResponse(updated), rawSecret);
    }
}
