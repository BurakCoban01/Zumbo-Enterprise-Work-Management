namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

internal static class SubscriptionResponseMapper
{
    public static WebhookSubscriptionResponse ToResponse(
        WebhookSubscriptionDocument document) => new(
        document.Id,
        document.Name,
        document.TargetUrl,
        document.EventScopes,
        document.IsActive,
        document.CurrentSecretFingerprint,
        document.SecretVersion,
        document.CreatedAtUtc,
        document.UpdatedAtUtc,
        document.Version);
}
