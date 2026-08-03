using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    private async Task<WebhookSubscriptionDocument> FindOwnedAsync(string id, CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        return await subscriptions.SelectAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
                "WEBHOOK_SUBSCRIPTION_NOT_FOUND", "Webhook subscription was not found.");
    }
}
