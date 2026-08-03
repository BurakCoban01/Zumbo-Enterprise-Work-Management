using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    public async Task DeleteConnectionAsync(
        string connectionId,
        long expectedVersion,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        if (connection.Version != expectedVersion) throw ConnectionConflict();
        await links.DeleteByFilterAsync(
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id,
            ct);
        await receipts.DeleteByFilterAsync(
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id,
            ct);
        await mappings.DeleteByFilterAsync(
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id,
            ct);
        var deleted = await connections.DeleteByFilterAsync(
            item => item.Id == connection.Id
                && item.OrganizationId == connection.OrganizationId
                && item.Version == expectedVersion,
            ct);
        if (deleted != 1) throw ConnectionConflict();
        await WriteAuditAsync(
            "DevelopmentConnectionDeleted",
            "DevelopmentConnection",
            connection.Id,
            $"{connection.Provider}|{connection.CredentialFingerprint}",
            null,
            correlationId,
            ct);
    }

}
