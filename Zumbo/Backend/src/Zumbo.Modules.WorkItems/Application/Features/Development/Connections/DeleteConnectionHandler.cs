using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

public sealed class DeleteConnectionHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDocumentRepository<DevelopmentRepositoryMappingDocument> mappings,
    IDocumentRepository<WorkItemDevelopmentLinkDocument> links,
    IDocumentRepository<DevelopmentWebhookReceiptDocument> receipts,
    IDevelopmentIntegrationAuthorization authorization,
    IWorkItemAuditPublisher audit,
    ICurrentUser currentUser)
{
    public async Task HandleAsync(
        DeleteConnectionCommand command,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(command.ConnectionId, ct);
        if (connection.Version != command.ExpectedVersion)
        {
            throw ConnectionConflict();
        }

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
                && item.Version == command.ExpectedVersion,
            ct);
        if (deleted != 1)
        {
            throw ConnectionConflict();
        }

        await audit.WriteAsync(
            "DevelopmentConnectionDeleted",
            "DevelopmentConnection",
            connection.Id,
            $"{connection.Provider}|{connection.CredentialFingerprint}",
            null,
            command.CorrelationId,
            ct);
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

    private static ConflictException ConnectionConflict() => new(
        "DEVELOPMENT_CONNECTION_CONFLICT",
        "Development connection changed concurrently; refresh and retry.");
}
