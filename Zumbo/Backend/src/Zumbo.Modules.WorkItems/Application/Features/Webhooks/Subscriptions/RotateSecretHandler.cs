using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

public sealed class RotateSecretHandler(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IWebhookSecretProtector secretProtector,
    IWebhookAuthorization authorization,
    IWorkItemAuditPublisher audit,
    IOptions<WebhookOptions> options,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<WebhookSecretReceipt> HandleAsync(
        RotateSecretCommand command,
        CancellationToken ct)
    {
        var document = await FindOwnedAsync(command.SubscriptionId, ct);
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
        var updated = await ReplaceAsync(
            document,
            command.Request.ExpectedVersion,
            ct);
        await audit.WriteAsync(
            "WebhookSecretRotated",
            "WebhookSubscription",
            updated.Id,
            document.PreviousSecretFingerprint,
            updated.CurrentSecretFingerprint,
            string.IsNullOrWhiteSpace(command.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : command.CorrelationId,
            ct);
        return new WebhookSecretReceipt(
            SubscriptionResponseMapper.ToResponse(updated),
            rawSecret);
    }

    private async Task<WebhookSubscriptionDocument> FindOwnedAsync(
        string id,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException(
                "Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        return await subscriptions.SelectAsync(
            item => item.Id == id && item.OrganizationId == organizationId,
            ct) ?? throw SubscriptionNotFound();
    }

    private async Task<WebhookSubscriptionDocument> ReplaceAsync(
        WebhookSubscriptionDocument document,
        long expectedVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await subscriptions.ReplaceByVersionAsync(
                item => item.Id == document.Id
                    && item.OrganizationId == document.OrganizationId,
                document,
                expectedVersion,
                ct);
            if (!result.Found)
            {
                throw SubscriptionNotFound();
            }

            document.Version = result.Version!.Value;
            return document;
        }
        catch (DocumentConcurrencyException)
        {
            throw new ConflictException(
                "WEBHOOK_SUBSCRIPTION_CONFLICT",
                "Webhook subscription changed concurrently; refresh and retry.");
        }
    }

    private static string GenerateSecret() =>
        "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Fingerprint(string value) => Hash(value)[..16];

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static NotFoundException SubscriptionNotFound() => new(
        "WEBHOOK_SUBSCRIPTION_NOT_FOUND",
        "Webhook subscription was not found.");
}
