namespace Zumbo.Modules.WorkItems;

public sealed record CreateDevelopmentConnectionRequest(
    string Name,
    string Provider,
    string BaseUrl,
    string AccessToken);

public sealed record RotateDevelopmentCredentialRequest(
    string AccessToken,
    long ExpectedVersion);

public sealed record DevelopmentVersionRequest(long ExpectedVersion);

public sealed record CreateDevelopmentRepositoryMappingRequest(
    string ProjectId,
    string ExternalRepositoryId,
    string RepositoryName,
    string RepositoryFullName,
    string RepositoryUrl,
    string DefaultBranch);

public sealed record CreateWorkItemDevelopmentLinkRequest(
    string MappingId,
    string Kind,
    string ExternalId,
    string Title,
    string Url,
    string? Branch,
    string? CommitSha,
    string Status);

public sealed record DevelopmentConnectionResponse(
    string Id,
    string Name,
    string Provider,
    string BaseUrl,
    string CredentialFingerprint,
    string WebhookSecretFingerprint,
    int WebhookSecretVersion,
    bool IsConnected,
    string HealthStatus,
    string? HealthErrorCode,
    DateTimeOffset? HealthCheckedAtUtc,
    DateTimeOffset? DisconnectedAtUtc,
    IReadOnlyCollection<string> RequiredScopes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

public sealed record DevelopmentConnectionReceipt(
    DevelopmentConnectionResponse Connection,
    string WebhookSecret);

public sealed record DevelopmentRepositoryResponse(
    string ExternalRepositoryId,
    string Name,
    string FullName,
    string Url,
    string DefaultBranch);

public sealed record DevelopmentRepositoryPage(
    IReadOnlyCollection<DevelopmentRepositoryResponse> Items,
    string SourceStatus);

public sealed record DevelopmentRepositoryMappingResponse(
    string Id,
    string ConnectionId,
    string ProjectId,
    string ProjectKey,
    string ProjectName,
    string ExternalRepositoryId,
    string RepositoryName,
    string RepositoryFullName,
    string RepositoryUrl,
    string DefaultBranch,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

public sealed record WorkItemDevelopmentLinkResponse(
    string Id,
    string ConnectionId,
    string MappingId,
    string ProjectId,
    string WorkItemId,
    string Provider,
    string RepositoryFullName,
    string Kind,
    string ExternalId,
    string Title,
    string Url,
    string? Branch,
    string? CommitSha,
    string Status,
    string Source,
    bool ConnectionActive,
    DateTimeOffset? LastEventAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

public sealed record DevelopmentHealthResponse(
    string Status,
    string? ErrorCode,
    DateTimeOffset CheckedAtUtc);

public sealed record DevelopmentWebhookResult(
    string Status,
    int AppliedLinks,
    bool Duplicate);

public sealed record DevelopmentWebhookEvent(
    string ReceiptId,
    string ConnectionId,
    long ConnectionLifecycleVersion,
    string OrganizationId,
    string DeliveryId,
    string ProviderEvent,
    NormalizedDevelopmentEvent? Event);

public sealed record DevelopmentProviderProbeResult(bool Healthy, string? SafeErrorCode);

public sealed record DevelopmentProviderRepository(
    string ExternalRepositoryId,
    string Name,
    string FullName,
    string Url,
    string DefaultBranch);

public sealed record DevelopmentProviderRepositoryResult(
    IReadOnlyCollection<DevelopmentProviderRepository> Items,
    bool Partial);

public sealed record DevelopmentProjectResource(
    string OrganizationId,
    string ProjectId,
    string ProjectKey,
    string ProjectName);

public sealed record DevelopmentWebhookRequest(
    string DeliveryId,
    string EventName,
    string? Timestamp,
    string Signature,
    byte[] Payload);

public sealed record NormalizedDevelopmentEvent(
    string RepositoryExternalId,
    string Kind,
    string ExternalId,
    string Title,
    string Url,
    string? Branch,
    string? CommitSha,
    string Status,
    DateTimeOffset? OccurredAtUtc,
    IReadOnlyCollection<string> ReferenceTexts);

public interface IDevelopmentCredentialProtector
{
    string Protect(string value);
    string Unprotect(string value);
}

public interface IDevelopmentIntegrationAuthorization
{
    Task EnsureCanManageAsync(string organizationId, CancellationToken ct);
}

public interface IDevelopmentProjectDirectory
{
    Task<DevelopmentProjectResource> GetAsync(
        string organizationId,
        string projectId,
        CancellationToken ct);
}

public interface IDevelopmentProviderGateway
{
    Task ValidateBaseUrlAsync(
        string provider,
        string baseUrl,
        CancellationToken ct);

    Task<DevelopmentProviderProbeResult> ProbeAsync(
        string provider,
        string baseUrl,
        string accessToken,
        CancellationToken ct);

    Task<DevelopmentProviderRepositoryResult> ListRepositoriesAsync(
        string provider,
        string baseUrl,
        string accessToken,
        int maximumItems,
        CancellationToken ct);
}

public interface IDevelopmentWebhookQueue
{
    Task EnqueueAsync(DevelopmentWebhookEvent message, CancellationToken ct);
}
