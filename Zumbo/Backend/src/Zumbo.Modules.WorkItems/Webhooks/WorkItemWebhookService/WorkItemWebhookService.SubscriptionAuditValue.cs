using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    private static string SubscriptionAuditValue(WebhookSubscriptionDocument document) =>
        $"{document.Name}|{new Uri(document.TargetUrl).Host}|{document.IsActive}|v{document.SecretVersion}"
        + $"|{string.Join(',', document.EventScopes)}";
}
