using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    private static WebhookSubscriptionResponse ToResponse(WebhookSubscriptionDocument document) => new(
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

    private static WebhookDeliveryResponse ToResponse(WebhookDeliveryDocument document) => new(
        document.Id,
        document.SubscriptionId,
        document.EventScope,
        document.PayloadSha256,
        document.Status,
        document.Attempts,
        document.NextAttemptAtUtc,
        document.LastErrorCode,
        document.DeliveredAtUtc,
        document.DeadLetteredAtUtc,
        document.CreatedAtUtc,
        document.Version);
}
