using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;

public sealed class RotateWebhookSecretHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDevelopmentCredentialProtector credentialProtector,
    IDevelopmentIntegrationAuthorization authorization,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<DevelopmentConnectionReceipt> HandleAsync(
        RotateWebhookSecretCommand command,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(command.ConnectionId, ct);
        EnsureConnected(connection);
        var secret = GenerateWebhookSecret(connection.Provider);
        connection.PreviousWebhookSecretProtected = connection.WebhookSecretProtected;
        connection.PreviousWebhookSecretVersion = connection.WebhookSecretVersion;
        connection.PreviousWebhookSecretValidUntilUtc = clock.UtcNow.AddMinutes(15);
        connection.WebhookSecretProtected = credentialProtector.Protect(secret);
        connection.WebhookSecretFingerprint = Fingerprint(secret);
        connection.WebhookSecretVersion++;
        connection.UpdatedAtUtc = clock.UtcNow;
        await ReplaceConnectionAsync(connection, command.Request.ExpectedVersion, ct);
        await audit.WriteAsync(
            "DevelopmentWebhookSecretRotated",
            "DevelopmentConnection",
            connection.Id,
            "previous-version",
            $"v{connection.WebhookSecretVersion}|{connection.WebhookSecretFingerprint}",
            command.CorrelationId,
            ct);
        return new DevelopmentConnectionReceipt(ToResponse(connection), secret);
    }

    private async Task<DevelopmentConnectionDocument> GetManagedConnectionAsync(
        string connectionId,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        return await connections.SelectAsync(
            item => item.Id == connectionId
                && item.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
                "DEVELOPMENT_CONNECTION_NOT_FOUND",
                "Development connection was not found.");
    }

    private async Task ReplaceConnectionAsync(
        DevelopmentConnectionDocument connection,
        long expectedVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await connections.ReplaceByVersionAsync(
                item => item.Id == connection.Id
                    && item.OrganizationId == connection.OrganizationId,
                connection,
                expectedVersion,
                ct);
            if (!result.Found)
            {
                throw new NotFoundException(
                    "DEVELOPMENT_CONNECTION_NOT_FOUND",
                    "Development connection was not found.");
            }

            connection.Version = result.Version!.Value;
        }
        catch (DocumentConcurrencyException)
        {
            throw new ConflictException(
                "DEVELOPMENT_CONNECTION_CONFLICT",
                "Development connection changed concurrently; refresh and retry.");
        }
    }

    private static void EnsureConnected(DevelopmentConnectionDocument connection)
    {
        if (!connection.IsConnected
            || string.IsNullOrWhiteSpace(connection.CredentialProtected)
            || string.IsNullOrWhiteSpace(connection.WebhookSecretProtected))
        {
            throw new ConflictException(
                "DEVELOPMENT_CONNECTION_DISCONNECTED",
                "The development connection is disconnected.");
        }
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
