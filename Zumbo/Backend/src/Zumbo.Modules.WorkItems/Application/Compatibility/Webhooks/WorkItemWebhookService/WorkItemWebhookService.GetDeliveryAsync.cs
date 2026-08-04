using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    public async Task<WebhookDeliveryResponse> GetDeliveryAsync(string id, CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var delivery = await deliveries.SelectAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            ct) ?? throw DeliveryNotFound();
        return ToResponse(delivery);
    }
}
