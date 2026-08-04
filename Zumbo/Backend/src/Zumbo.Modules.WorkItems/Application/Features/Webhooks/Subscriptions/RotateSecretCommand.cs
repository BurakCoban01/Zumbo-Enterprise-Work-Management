namespace Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;

public sealed record RotateSecretCommand(
    string SubscriptionId,
    RotateWebhookSecretRequest Request,
    string? CorrelationId);
