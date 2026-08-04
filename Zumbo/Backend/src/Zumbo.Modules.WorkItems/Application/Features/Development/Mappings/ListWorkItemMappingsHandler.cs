using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;

public sealed class ListWorkItemMappingsHandler(
    IDocumentRepository<WorkItemDocument> workItems,
    IDocumentRepository<DevelopmentRepositoryMappingDocument> mappings,
    IProjectPermissionChecker projectPermissions,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyCollection<DevelopmentRepositoryMappingResponse>> HandleAsync(
        ListWorkItemMappingsQuery query,
        CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(query.WorkItemId, ct);
        var documents = await ListAllAsync(
            mappings,
            item => item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.IsActive,
            ct);
        return documents
            .OrderBy(item => item.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();
    }

    private async Task<(WorkItemDocument WorkItem, string OrganizationId)> GetWorkItemAsync(
        string workItemId,
        CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Authenticated organization is required.");
        var workItem = await workItems.SelectAsync(
            item => item.Id == workItemId && !item.Archived,
            ct) ?? throw new NotFoundException(
                "WORK_ITEM_NOT_FOUND",
                "Work item was not found.");
        var access = await projectPermissions.EnsureCanAsync(
            userId,
            workItem.ProjectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (!string.Equals(access.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        }

        return (workItem, access.OrganizationId);
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
