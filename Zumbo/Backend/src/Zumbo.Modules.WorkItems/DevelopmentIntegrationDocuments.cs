using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public static class DevelopmentProviders
{
    public const string GitHub = "GitHub";
    public const string GitLab = "GitLab";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [GitHub, GitLab],
        StringComparer.Ordinal);
}

public static class DevelopmentLinkKinds
{
    public const string Branch = "Branch";
    public const string Commit = "Commit";
    public const string PullRequest = "PullRequest";
    public const string Build = "Build";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Branch, Commit, PullRequest, Build],
        StringComparer.Ordinal);
}

public static class DevelopmentWebhookReceiptStatuses
{
    public const string Pending = "Pending";
    public const string Applied = "Applied";
    public const string Ignored = "Ignored";
}

public static class DevelopmentIntegrationLimits
{
    public const int MaximumConnectionsPerOrganization = 20;
    public const int MaximumMappingsPerConnection = 100;
    public const int MaximumLinksPerWorkItem = 50;
    public const int MaximumProviderRepositories = 100;
    public const int MaximumWorkItemReferencesPerEvent = 10;
    public const int MaximumWebhookPayloadBytes = 1_048_576;
    public const int DeliveryRetentionDays = 90;
    public const int ReplayWindowSeconds = 300;
}

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

public sealed class DevelopmentRepositoryMappingDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectKey { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ExternalRepositoryId { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string RepositoryFullName { get; set; } = string.Empty;
    public string RepositoryUrl { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed class WorkItemDevelopmentLinkDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string MappingId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string Provider { get; set; } = DevelopmentProviders.GitHub;
    public string RepositoryFullName { get; set; } = string.Empty;
    public string Kind { get; set; } = DevelopmentLinkKinds.PullRequest;
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Branch { get; set; }
    public string? CommitSha { get; set; }
    public string Status { get; set; } = "Unknown";
    public string Source { get; set; } = "Manual";
    public DateTimeOffset? LastEventAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed class DevelopmentWebhookReceiptDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string DeliveryId { get; set; } = string.Empty;
    public string ProviderEvent { get; set; } = string.Empty;
    public string PayloadSha256 { get; set; } = string.Empty;
    public string Status { get; set; } = DevelopmentWebhookReceiptStatuses.Pending;
    public int AppliedLinks { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public long Version { get; set; }
}
