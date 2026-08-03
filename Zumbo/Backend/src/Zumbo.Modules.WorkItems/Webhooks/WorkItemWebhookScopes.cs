using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemWebhookScopes
{
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
    [
        "work-item.created",
        "work-item.updated",
        "work-item.moved",
        "work-item.reordered",
        "work-item.archived",
        "work-item.restored"
    ], StringComparer.Ordinal);

    public static string FromEventType(string eventType) => "work-item." + eventType.Trim().ToLowerInvariant();
}
