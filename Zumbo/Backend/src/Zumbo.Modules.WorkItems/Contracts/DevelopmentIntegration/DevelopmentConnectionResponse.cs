namespace Zumbo.Modules.WorkItems;

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
