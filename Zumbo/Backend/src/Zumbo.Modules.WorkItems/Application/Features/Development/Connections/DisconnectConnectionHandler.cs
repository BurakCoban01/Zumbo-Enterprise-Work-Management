using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

public sealed class DisconnectConnectionHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDocumentRepository<DevelopmentRepositoryMappingDocument> mappings,
    IDevelopmentIntegrationAuthorization authorization,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<DevelopmentConnectionResponse> HandleAsync(
        DisconnectConnectionCommand command,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(command.ConnectionId, ct);
        if (!connection.IsConnected)
        {
            return ToResponse(connection);
        }

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
        await ReplaceConnectionAsync(connection, command.Request.ExpectedVersion, ct);

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

        await audit.WriteAsync(
            "DevelopmentConnectionDisconnected",
            "DevelopmentConnection",
            connection.Id,
            "Connected",
            "Disconnected",
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

    private async Task ReplaceMappingAsync(
        DevelopmentRepositoryMappingDocument mapping,
        long expectedVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await mappings.ReplaceByVersionAsync(
                item => item.Id == mapping.Id
                    && item.OrganizationId == mapping.OrganizationId,
                mapping,
                expectedVersion,
                ct);
            if (!result.Found)
            {
                throw new NotFoundException(
                    "DEVELOPMENT_REPOSITORY_MAPPING_NOT_FOUND",
                    "Development repository mapping was not found.");
            }

            mapping.Version = result.Version!.Value;
        }
        catch (DocumentConcurrencyException)
        {
            throw new ConflictException(
                "DEVELOPMENT_MAPPING_CONFLICT",
                "Development repository mapping changed concurrently; refresh and retry.");
        }
    }

    private static async Task<List<TDocument>> ListAllAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var result = new List<TDocument>();
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return result;
    }

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
