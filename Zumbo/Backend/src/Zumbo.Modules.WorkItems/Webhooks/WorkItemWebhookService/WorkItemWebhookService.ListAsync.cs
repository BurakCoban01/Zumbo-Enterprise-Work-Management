using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<IReadOnlyList<WebhookSubscriptionResponse>> ListAsync(CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var result = new List<WebhookSubscriptionResponse>();
        string? cursor = null;
        do
        {
            var page = await subscriptions.ListByCursorAsync(
                x => x.OrganizationId == organizationId, cursor, 200, ct);
            result.AddRange(page.Items.Select(ToResponse));
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return result;
    }
}
