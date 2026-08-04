using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

public sealed class GetConnectionHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDevelopmentIntegrationAuthorization authorization,
    ICurrentUser currentUser)
{
    public async Task<DevelopmentConnectionResponse> HandleAsync(
        GetConnectionQuery query,
        CancellationToken ct) =>
        ToResponse(await GetManagedConnectionAsync(query.ConnectionId, ct));

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
