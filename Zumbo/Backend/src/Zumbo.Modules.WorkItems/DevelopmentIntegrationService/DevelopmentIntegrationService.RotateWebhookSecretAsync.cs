using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentConnectionReceipt> RotateWebhookSecretAsync(
        string connectionId,
        DevelopmentVersionRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        EnsureConnected(connection);
        var secret = GenerateWebhookSecret(connection.Provider);
        connection.PreviousWebhookSecretProtected = connection.WebhookSecretProtected;
        connection.PreviousWebhookSecretVersion = connection.WebhookSecretVersion;
        connection.PreviousWebhookSecretValidUntilUtc = clock.UtcNow.AddMinutes(15);
        connection.WebhookSecretProtected = credentialProtector.Protect(secret);
        connection.WebhookSecretFingerprint = Fingerprint(secret);
        connection.WebhookSecretVersion++;
        connection.UpdatedAtUtc = clock.UtcNow;
        await ReplaceConnectionAsync(connection, request.ExpectedVersion, ct);
        await WriteAuditAsync(
            "DevelopmentWebhookSecretRotated",
            "DevelopmentConnection",
            connection.Id,
            "previous-version",
            $"v{connection.WebhookSecretVersion}|{connection.WebhookSecretFingerprint}",
            correlationId,
            ct);
        return new DevelopmentConnectionReceipt(ToResponse(connection), secret);
    }

}
