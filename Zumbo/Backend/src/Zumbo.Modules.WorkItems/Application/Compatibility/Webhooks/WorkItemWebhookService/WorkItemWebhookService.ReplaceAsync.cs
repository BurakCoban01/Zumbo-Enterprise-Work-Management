using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    private async Task<WebhookSubscriptionDocument> ReplaceAsync(
        WebhookSubscriptionDocument document,
        long expectedVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await subscriptions.ReplaceByVersionAsync(
                x => x.Id == document.Id && x.OrganizationId == document.OrganizationId,
                document,
                expectedVersion,
                ct);
            if (!result.Found) throw new NotFoundException(
                "WEBHOOK_SUBSCRIPTION_NOT_FOUND", "Webhook subscription was not found.");
            document.Version = result.Version!.Value;
            return document;
        }
        catch (DocumentConcurrencyException)
        {
            throw new ConflictException(
                "WEBHOOK_SUBSCRIPTION_CONFLICT", "Webhook subscription changed concurrently; refresh and retry.");
        }
    }
}
