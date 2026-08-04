using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;

namespace Zumbo.Api.Infrastructure.BackgroundServices.Webhooks;

public sealed class DevelopmentWebhookProcessingDurableHandler(
    ProcessWebhookHandler handler) : IDurableEventHandler
{
    public string ConsumerName => "work-item-development-webhook-v1";
    public string EventType => WorkItemDurableEventTypes.DevelopmentWebhook;

    public Task HandleAsync(
        DurableEventEnvelope message,
        CancellationToken cancellationToken) =>
        handler.HandleAsync(
            new ProcessWebhookCommand(
                DurablePayload.Read<DevelopmentWebhookEvent>(message)),
            cancellationToken);
}
