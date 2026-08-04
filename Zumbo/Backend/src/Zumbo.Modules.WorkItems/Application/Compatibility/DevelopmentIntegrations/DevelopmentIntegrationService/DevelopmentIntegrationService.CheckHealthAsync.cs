using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentHealthResponse> CheckHealthAsync(
        string connectionId,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
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
        await WriteAuditAsync(
            "DevelopmentConnectionHealthChecked",
            "DevelopmentConnection",
            connection.Id,
            null,
            $"{connection.HealthStatus}|{connection.HealthErrorCode}",
            correlationId,
            ct);
        return new DevelopmentHealthResponse(
            connection.HealthStatus,
            connection.HealthErrorCode,
            connection.HealthCheckedAtUtc.Value);
    }

}
