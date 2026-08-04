using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    private static void ClearLease(WebhookDeliveryDocument document)
    {
        document.LeaseToken = null;
        document.ClaimedBy = null;
        document.LeaseUntilUtc = null;
    }
}
