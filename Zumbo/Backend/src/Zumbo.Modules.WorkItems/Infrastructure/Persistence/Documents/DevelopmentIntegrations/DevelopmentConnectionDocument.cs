using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class DevelopmentConnectionDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = DevelopmentProviders.GitHub;
    public string BaseUrl { get; set; } = string.Empty;
    public string CredentialProtected { get; set; } = string.Empty;
    public string CredentialFingerprint { get; set; } = string.Empty;
    public string WebhookSecretProtected { get; set; } = string.Empty;
    public string WebhookSecretFingerprint { get; set; } = string.Empty;
    public int WebhookSecretVersion { get; set; } = 1;
    public long LifecycleVersion { get; set; } = 1;
    public string? PreviousWebhookSecretProtected { get; set; }
    public int? PreviousWebhookSecretVersion { get; set; }
    public DateTimeOffset? PreviousWebhookSecretValidUntilUtc { get; set; }
    public bool IsConnected { get; set; } = true;
    public string HealthStatus { get; set; } = "NotChecked";
    public string? HealthErrorCode { get; set; }
    public DateTimeOffset? HealthCheckedAtUtc { get; set; }
    public DateTimeOffset? DisconnectedAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}
