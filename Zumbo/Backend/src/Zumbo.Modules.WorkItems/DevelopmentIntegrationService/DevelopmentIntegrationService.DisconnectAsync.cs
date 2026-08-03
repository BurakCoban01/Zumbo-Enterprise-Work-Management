using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task<DevelopmentConnectionResponse> DisconnectAsync(
        string connectionId,
        DevelopmentVersionRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        if (!connection.IsConnected) return ToResponse(connection);
        connection.IsConnected = false;
        connection.LifecycleVersion++;
        connection.CredentialProtected = string.Empty;
        connection.WebhookSecretProtected = string.Empty;
        connection.PreviousWebhookSecretProtected = null;
        connection.PreviousWebhookSecretVersion = null;
        connection.PreviousWebhookSecretValidUntilUtc = null;
        connection.HealthStatus = "Disconnected";
        connection.HealthErrorCode = null;
        connection.DisconnectedAtUtc = clock.UtcNow;
        connection.UpdatedAtUtc = clock.UtcNow;
        await ReplaceConnectionAsync(connection, request.ExpectedVersion, ct);
        var ownedMappings = await ListAllAsync(
            mappings,
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id
                && item.IsActive,
            ct);
        foreach (var mapping in ownedMappings)
        {
            mapping.IsActive = false;
            mapping.UpdatedAtUtc = clock.UtcNow;
            await ReplaceMappingAsync(mapping, mapping.Version, ct);
        }
        await WriteAuditAsync(
            "DevelopmentConnectionDisconnected",
            "DevelopmentConnection",
            connection.Id,
            "Connected",
            "Disconnected",
            correlationId,
            ct);
        return ToResponse(connection);
    }

}
