using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

public sealed class ListConnectionMappingsHandler(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDocumentRepository<DevelopmentRepositoryMappingDocument> mappings,
    IDevelopmentIntegrationAuthorization authorization,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyCollection<DevelopmentRepositoryMappingResponse>> HandleAsync(
        ListConnectionMappingsQuery query,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(query.ConnectionId, ct);
        var documents = await ListAllAsync(
            mappings,
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id,
            ct);
        return documents
            .OrderBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();
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

    private static DevelopmentRepositoryMappingResponse ToResponse(
        DevelopmentRepositoryMappingDocument document) =>
        new(
            document.Id,
            document.ConnectionId,
            document.ProjectId,
            document.ProjectKey,
            document.ProjectName,
            document.ExternalRepositoryId,
            document.RepositoryName,
            document.RepositoryFullName,
            document.RepositoryUrl,
            document.DefaultBranch,
            document.IsActive,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.Version);
}
