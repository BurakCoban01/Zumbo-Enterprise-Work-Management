using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Links;

public sealed class ListWorkItemLinksHandler(
    IDocumentRepository<WorkItemDocument> workItems,
    IDocumentRepository<WorkItemDevelopmentLinkDocument> links,
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IProjectPermissionChecker projectPermissions,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyCollection<WorkItemDevelopmentLinkResponse>> HandleAsync(
        ListWorkItemLinksQuery query,
        CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(query.WorkItemId, ct);
        var documents = await ListAllAsync(
            links,
            item => item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.WorkItemId == workItem.Id,
            ct);
        var connectionStates = await ConnectionStatesAsync(
            organizationId,
            documents.Select(item => item.ConnectionId),
            ct);
        return documents
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Select(item => ToResponse(
                item,
                connectionStates.GetValueOrDefault(item.ConnectionId)))
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
            PermissionCatalog.WorkItemView,
            ct);
        if (!string.Equals(access.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        }

        return (workItem, access.OrganizationId);
    }

    private async Task<Dictionary<string, bool>> ConnectionStatesAsync(
        string organizationId,
        IEnumerable<string> connectionIds,
        CancellationToken ct)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var connectionId in connectionIds.Distinct(StringComparer.Ordinal))
        {
            var connection = await connections.SelectAsync(
                item => item.Id == connectionId
                    && item.OrganizationId == organizationId,
                ct);
            result[connectionId] = connection?.IsConnected == true;
        }

        return result;
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

    private static WorkItemDevelopmentLinkResponse ToResponse(
        WorkItemDevelopmentLinkDocument document,
        bool connectionActive) =>
        new(
            document.Id,
            document.ConnectionId,
            document.MappingId,
            document.ProjectId,
            document.WorkItemId,
            document.Provider,
            document.RepositoryFullName,
            document.Kind,
            document.ExternalId,
            document.Title,
            document.Url,
            document.Branch,
            document.CommitSha,
            document.Status,
            document.Source,
            connectionActive,
            document.LastEventAtUtc,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.Version);
}
