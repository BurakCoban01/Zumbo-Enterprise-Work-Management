using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentConnectionResponse> RotateCredentialAsync(
        string connectionId,
        RotateDevelopmentCredentialRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        EnsureConnected(connection);
        var credential = RequireSecret(request.AccessToken, "Provider access token");
        var previous = connection.CredentialFingerprint;
        connection.CredentialProtected = credentialProtector.Protect(credential);
        connection.CredentialFingerprint = Fingerprint(credential);
        connection.HealthStatus = "NotChecked";
        connection.HealthErrorCode = null;
        connection.HealthCheckedAtUtc = null;
        connection.UpdatedAtUtc = clock.UtcNow;
        await ReplaceConnectionAsync(connection, request.ExpectedVersion, ct);
        await WriteAuditAsync(
            "DevelopmentCredentialRotated",
            "DevelopmentConnection",
            connection.Id,
            previous,
            connection.CredentialFingerprint,
            correlationId,
            ct);
        return ToResponse(connection);
    }

}
