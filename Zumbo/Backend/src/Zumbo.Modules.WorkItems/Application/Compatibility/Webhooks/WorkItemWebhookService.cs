using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Deliveries;
using Zumbo.Modules.WorkItems.Application.Features.Webhooks.Subscriptions;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService(
    IDocumentRepository<WebhookSubscriptionDocument> subscriptions,
    IDocumentRepository<WebhookDeliveryDocument> deliveries,
    IWebhookSecretProtector secretProtector,
    IWebhookTargetPolicy targetPolicy,
    IWebhookSender sender,
    IWebhookAuthorization authorization,
    IWorkItemAuditPublisher audit,
    IOptions<WebhookOptions> options,
    IClock clock,
    ICurrentUser currentUser,
    IDurableMessageJitter? retryJitter = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ListWebhookSubscriptionsHandler listHandler = new(
        subscriptions,
        authorization,
        currentUser);
    private readonly GetWebhookSubscriptionHandler getHandler = new(
        subscriptions,
        authorization,
        currentUser);
    private readonly GetWebhookDeliveryMetricsHandler metricsHandler = new(
        deliveries,
        authorization,
        currentUser,
        clock);
    private readonly ListWebhookDeliveriesHandler listDeliveriesHandler = new(
        subscriptions,
        deliveries,
        authorization,
        currentUser);
    private readonly GetWebhookDeliveryHandler getDeliveryHandler = new(
        deliveries,
        authorization,
        currentUser);
    private readonly ReplayWebhookDeliveryHandler replayHandler = new(
        deliveries,
        authorization,
        audit,
        clock,
        currentUser);
    private readonly SetSubscriptionStateHandler stateHandler = new(
        subscriptions,
        authorization,
        audit,
        clock,
        currentUser);
    private readonly UpdateSubscriptionHandler updateHandler = new(
        subscriptions,
        targetPolicy,
        authorization,
        audit,
        clock,
        currentUser);
    private readonly CreateSubscriptionHandler createHandler = new(
        subscriptions,
        secretProtector,
        targetPolicy,
        authorization,
        audit,
        clock,
        currentUser);
    private readonly RotateSecretHandler rotateSecretHandler = new(
        subscriptions,
        secretProtector,
        authorization,
        audit,
        options,
        clock,
        currentUser);
    private readonly QueueTestDeliveryHandler queueTestDeliveryHandler = new(
        subscriptions,
        deliveries,
        authorization,
        audit,
        clock,
        currentUser);
    private readonly QueueDeliveryHandler queueDeliveryHandler = new(
        subscriptions,
        deliveries,
        clock);
    private readonly DispatchDeliveriesHandler dispatchDeliveriesHandler = new(
        subscriptions,
        deliveries,
        secretProtector,
        targetPolicy,
        sender,
        options,
        clock,
        retryJitter);
}
