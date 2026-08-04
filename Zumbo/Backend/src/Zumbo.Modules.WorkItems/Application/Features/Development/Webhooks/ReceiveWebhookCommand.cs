namespace Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;

public sealed record ReceiveWebhookCommand(
    string ConnectionId,
    DevelopmentWebhookRequest Request);
