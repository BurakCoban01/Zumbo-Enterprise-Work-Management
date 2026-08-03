using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
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
}
