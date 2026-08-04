using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Repositories;

public sealed class ListRepositoriesHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDevelopmentCredentialProtector credentialProtector,
    IDevelopmentIntegrationAuthorization authorization,
    IDevelopmentProviderGateway providerGateway,
    ICurrentUser currentUser)
{
    public async Task<DevelopmentProviderRepositoryResult> HandleAsync(
        ListRepositoriesQuery query,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(query.ConnectionId, ct);
        EnsureConnected(connection);
        return await providerGateway.ListRepositoriesAsync(
            connection.Provider,
            connection.BaseUrl,
            credentialProtector.Unprotect(connection.CredentialProtected),
            DevelopmentIntegrationLimits.MaximumProviderRepositories,
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
}
