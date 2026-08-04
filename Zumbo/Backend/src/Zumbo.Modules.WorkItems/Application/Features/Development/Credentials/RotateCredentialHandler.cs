using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;

public sealed class RotateCredentialHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDevelopmentCredentialProtector credentialProtector,
    IDevelopmentIntegrationAuthorization authorization,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<DevelopmentConnectionResponse> HandleAsync(
        RotateCredentialCommand command,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(command.ConnectionId, ct);
        EnsureConnected(connection);
        var credential = RequireSecret(command.Request.AccessToken, "Provider access token");
        var previous = connection.CredentialFingerprint;
        connection.CredentialProtected = credentialProtector.Protect(credential);
        connection.CredentialFingerprint = Fingerprint(credential);
        connection.HealthStatus = "NotChecked";
        connection.HealthErrorCode = null;
        connection.HealthCheckedAtUtc = null;
        connection.UpdatedAtUtc = clock.UtcNow;
        await ReplaceConnectionAsync(connection, command.Request.ExpectedVersion, ct);
        await audit.WriteAsync(
            "DevelopmentCredentialRotated",
            "DevelopmentConnection",
            connection.Id,
            previous,
            connection.CredentialFingerprint,
            command.CorrelationId,
            ct);
        return ToResponse(connection);
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
