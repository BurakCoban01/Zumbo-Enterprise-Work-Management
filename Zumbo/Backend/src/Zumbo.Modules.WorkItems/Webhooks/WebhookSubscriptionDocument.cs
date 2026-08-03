using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WebhookSubscriptionDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public List<string> EventScopes { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public string CurrentSecretProtected { get; set; } = string.Empty;
    public string CurrentSecretFingerprint { get; set; } = string.Empty;
    public int SecretVersion { get; set; } = 1;
    public string? PreviousSecretProtected { get; set; }
    public string? PreviousSecretFingerprint { get; set; }
    public int? PreviousSecretVersion { get; set; }
    public DateTimeOffset? PreviousSecretValidUntilUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}
