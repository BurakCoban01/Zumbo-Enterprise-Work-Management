using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.ProviderHealth;

public sealed class CheckProviderHealthHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDevelopmentCredentialProtector credentialProtector,
    IDevelopmentIntegrationAuthorization authorization,
    IDevelopmentProviderGateway providerGateway,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<DevelopmentHealthResponse> HandleAsync(
        CheckProviderHealthCommand command,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(command.ConnectionId, ct);
        EnsureConnected(connection);
        var result = await providerGateway.ProbeAsync(
            connection.Provider,
            connection.BaseUrl,
            credentialProtector.Unprotect(connection.CredentialProtected),
            ct);
        connection.HealthStatus = result.Healthy ? "Healthy" : "Degraded";
        connection.HealthErrorCode = result.Healthy
            ? null
            : Optional(result.SafeErrorCode, "Health error code", 80) ?? "PROVIDER_UNAVAILABLE";
        connection.HealthCheckedAtUtc = clock.UtcNow;
        connection.UpdatedAtUtc = clock.UtcNow;
        await ReplaceConnectionAsync(connection, connection.Version, ct);
        await audit.WriteAsync(
            "DevelopmentConnectionHealthChecked",
            "DevelopmentConnection",
            connection.Id,
            null,
            $"{connection.HealthStatus}|{connection.HealthErrorCode}",
            command.CorrelationId,
            ct);
        return new DevelopmentHealthResponse(
            connection.HealthStatus,
            connection.HealthErrorCode,
            connection.HealthCheckedAtUtc.Value);
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

    private static string? Optional(string? value, string label, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximum)
        {
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        }

        return normalized;
    }
}
