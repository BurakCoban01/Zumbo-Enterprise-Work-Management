using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Connections;

public sealed class ListConnectionsHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDevelopmentIntegrationAuthorization authorization,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyCollection<DevelopmentConnectionResponse>> HandleAsync(
        ListConnectionsQuery query,
        CancellationToken ct)
    {
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Authenticated organization is required.");
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var documents = await ListAllAsync(
            connections,
            item => item.OrganizationId == organizationId,
            ct);
        return documents
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Select(ToResponse)
            .ToList();
    }

    private static async Task<List<DevelopmentConnectionDocument>> ListAllAsync(
        IDocumentRepository<DevelopmentConnectionDocument> repository,
        System.Linq.Expressions.Expression<Func<DevelopmentConnectionDocument, bool>> filter,
        CancellationToken ct)
    {
        var result = new List<DevelopmentConnectionDocument>();
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
