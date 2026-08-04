using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

public sealed class CreateConnectionHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDevelopmentCredentialProtector credentialProtector,
    IDevelopmentIntegrationAuthorization authorization,
    IDevelopmentProviderGateway providerGateway,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<DevelopmentConnectionReceipt> HandleAsync(
        CreateConnectionCommand command,
        CancellationToken ct)
    {
        var request = command.Request;
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        if (await connections.CountByFilterAsync(
                item => item.OrganizationId == organizationId,
                ct) >= DevelopmentIntegrationLimits.MaximumConnectionsPerOrganization)
        {
            throw new ValidationException(
                $"An organization cannot contain more than {DevelopmentIntegrationLimits.MaximumConnectionsPerOrganization} development connections.");
        }

        var provider = NormalizeProvider(request.Provider);
        var credential = RequireSecret(request.AccessToken, "Provider access token");
        var webhookSecret = GenerateWebhookSecret(provider);
        var baseUrl = NormalizeBaseUrl(provider, request.BaseUrl);
        await providerGateway.ValidateBaseUrlAsync(provider, baseUrl, ct);
        var now = clock.UtcNow;
        var document = await connections.CreateAsync(new DevelopmentConnectionDocument
        {
            OrganizationId = organizationId,
            Name = Required(request.Name, "Connection name", 100),
            Provider = provider,
            BaseUrl = baseUrl,
            CredentialProtected = credentialProtector.Protect(credential),
            CredentialFingerprint = Fingerprint(credential),
            WebhookSecretProtected = credentialProtector.Protect(webhookSecret),
            WebhookSecretFingerprint = Fingerprint(webhookSecret),
            CreatedByUserId = currentUser.UserId
                ?? throw new UnauthorizedException("Authenticated user is required."),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        await audit.WriteAsync(
            "DevelopmentConnectionCreated",
            "DevelopmentConnection",
            document.Id,
            null,
            $"{document.Provider}|{new Uri(document.BaseUrl).Host}|{document.CredentialFingerprint}",
            command.CorrelationId,
            ct);
        return new DevelopmentConnectionReceipt(ToResponse(document), webhookSecret);
    }

    private static string NormalizeProvider(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var provider = DevelopmentProviders.All.FirstOrDefault(
            item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return provider ?? throw new ValidationException(
            "Development provider must be GitHub or GitLab.");
    }

    private static string NormalizeBaseUrl(string provider, string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? provider == DevelopmentProviders.GitHub
                ? "https://api.github.com"
                : "https://gitlab.com/api/v4"
            : value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || !string.IsNullOrWhiteSpace(uri.Query)
            || !string.IsNullOrWhiteSpace(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ValidationException(
                "Development provider base URL must be an absolute HTTP(S) URL without credentials, query or fragment.");
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string GenerateWebhookSecret(string provider)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return provider == DevelopmentProviders.GitLab
            ? "whsec_" + Convert.ToBase64String(bytes)
            : "ghsec_" + Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
    }

    private static string RequireSecret(string value, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 16 or > 512
            || normalized.Any(char.IsWhiteSpace))
        {
            throw new ValidationException(
                $"{label} must contain between 16 and 512 non-whitespace characters.");
        }

        return normalized;
    }

    private static string Required(string value, string label, int maximum)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maximum)
        {
            throw new ValidationException(
                $"{label} must contain between 1 and {maximum} characters.");
        }

        return normalized;
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..16];

    private static DevelopmentConnectionResponse ToResponse(
        DevelopmentConnectionDocument document) =>
        new(
            document.Id,
            document.Name,
            document.Provider,
            document.BaseUrl,
            document.CredentialFingerprint,
            document.WebhookSecretFingerprint,
            document.WebhookSecretVersion,
            document.IsConnected,
            document.HealthStatus,
            document.HealthErrorCode,
            document.HealthCheckedAtUtc,
            document.DisconnectedAtUtc,
            RequiredScopes(document.Provider),
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.Version);

    private static IReadOnlyCollection<string> RequiredScopes(string provider) =>
        provider == DevelopmentProviders.GitHub
            ? ["metadata:read", "pull_requests:read", "commit_statuses:read"]
            : ["read_api"];
}
