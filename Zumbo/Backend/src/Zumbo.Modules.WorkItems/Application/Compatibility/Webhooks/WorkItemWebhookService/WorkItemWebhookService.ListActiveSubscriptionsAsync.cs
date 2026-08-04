using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    private async Task<IReadOnlyList<WebhookSubscriptionDocument>> ListActiveSubscriptionsAsync(
        string organizationId,
        CancellationToken ct)
    {
        var result = new List<WebhookSubscriptionDocument>();
        string? cursor = null;
        do
        {
            var page = await subscriptions.ListByCursorAsync(
                x => x.OrganizationId == organizationId && x.IsActive,
                cursor,
                200,
                ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return result;
    }
}
