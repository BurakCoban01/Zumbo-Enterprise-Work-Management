using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class SearchWorkItemsSlice(
    IDocumentRepository<WorkItemDocument> workItems,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemTypeSchemaPolicy typeSchemas,
    IWorkItemSearchIndex searchIndex,
    IWorkItemActivityStore activityStore,
    IOptions<SearchOptions> searchOptions)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly SearchOptions searchRuntimeOptions = searchOptions.Value;

    internal async Task<IReadOnlyList<WorkItemResponse>> HandleAsync(
        WorkItemSearchRequest request,
        CancellationToken ct) =>
        (await SearchPageAsync(request, ct)).Items;

    private async Task<WorkItemSearchPageResponse> SearchPageAsync(
        WorkItemSearchRequest request,
        CancellationToken ct)
    {
        SearchWorkItemsValidator.Validate(request);
        await EnsurePermissionAsync(request.ProjectId!, "WorkItemView", ct);
        var searchFilter = await typeSchemas.ValidateSearchFilterAsync(
            request.ProjectId!, request.IssueType, request.CustomFieldKey, request.CustomFieldValue, ct);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var text = request.Text?.Trim().ToLowerInvariant();

        if (!request.Archived && (!string.IsNullOrWhiteSpace(text)
            || !string.IsNullOrWhiteSpace(searchFilter.IssueType)
            || !string.IsNullOrWhiteSpace(searchFilter.CustomFieldKey)))
        {
            WorkItemSearchResult searchResult;
            var query = new WorkItemSearchQuery(
                CurrentOrganizationId(request.ProjectId!), request.ProjectId!, text,
                request.AssigneeUserId, request.Status, page, pageSize,
                searchFilter.IssueType, searchFilter.CustomFieldKey, searchFilter.CustomFieldValue);
            try
            {
                searchResult = await searchIndex.SearchAsync(query, ct);
            }
            catch (WorkItemSearchUnavailableException)
            {
                return await SearchDegradedAsync(request, searchFilter, text, page, pageSize, ct);
            }

            var ids = searchResult.Ids;
            if (ids.Count == 0)
            {
                return new WorkItemSearchPageResponse([], searchResult.TotalCount, false);
            }

            var idSet = ids.ToHashSet(StringComparer.Ordinal);
            var indexedResult = await workItems.ListByFilterAsync(
                x => !x.Archived && x.ProjectId == request.ProjectId && idSet.Contains(x.Id),
                pageSize: 200,
                cancellationToken: ct);
            var resultById = indexedResult.ToDictionary(x => x.Id, StringComparer.Ordinal);
            await HydrateAllAsync(resultById.Values, ct);
            var items = ids.Where(resultById.ContainsKey)
                .Select(id => WorkItemResponseMapper.ToResponse(resultById[id])).ToList();
            return new WorkItemSearchPageResponse(items, searchResult.TotalCount, false);
        }

        var result = await workItems.ListByFilterAsync(
            x => x.Archived == request.Archived
                && x.ProjectId == request.ProjectId
                && (string.IsNullOrEmpty(request.AssigneeUserId) || x.AssigneeUserId == request.AssigneeUserId)
                && (string.IsNullOrEmpty(request.Status) || x.Status == request.Status)
                && (string.IsNullOrEmpty(request.IssueType) || x.Type == request.IssueType)
                && (string.IsNullOrEmpty(text) || x.Title.ToLower().Contains(text) || x.Description.ToLower().Contains(text)),
            x => x.Rank,
            page: page,
            pageSize: pageSize,
            cancellationToken: ct);
        var totalCount = await workItems.CountByFilterAsync(
            x => x.Archived == request.Archived
                && x.ProjectId == request.ProjectId
                && (string.IsNullOrEmpty(request.AssigneeUserId) || x.AssigneeUserId == request.AssigneeUserId)
                && (string.IsNullOrEmpty(request.Status) || x.Status == request.Status)
                && (string.IsNullOrEmpty(request.IssueType) || x.Type == request.IssueType)
                && (string.IsNullOrEmpty(text) || x.Title.ToLower().Contains(text) || x.Description.ToLower().Contains(text)),
            ct);
        await HydrateAllAsync(result, ct);
        return new WorkItemSearchPageResponse(
            result.Select(WorkItemResponseMapper.ToResponse).ToList(), totalCount, false);
    }

    private async Task<WorkItemSearchPageResponse> SearchDegradedAsync(
        WorkItemSearchRequest request,
        ValidatedWorkItemSearchFilter searchFilter,
        string? text,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var maximum = Math.Clamp(searchRuntimeOptions.DegradedFallbackMaxItems, 1, 10_000);
        var candidates = new List<WorkItemDocument>(Math.Min(maximum, 200));
        string? cursor = null;
        while (candidates.Count < maximum)
        {
            var batch = await workItems.ListByCursorAsync(
                x => !x.Archived && x.ProjectId == request.ProjectId,
                cursor, Math.Min(200, maximum - candidates.Count), ct);
            candidates.AddRange(batch.Items);
            cursor = batch.NextCursor;
            if (cursor is null) break;
        }

        var matches = candidates
            .Where(x => string.IsNullOrEmpty(request.AssigneeUserId) || x.AssigneeUserId == request.AssigneeUserId)
            .Where(x => string.IsNullOrEmpty(request.Status) || x.Status == request.Status)
            .Where(x => string.IsNullOrEmpty(searchFilter.IssueType) || x.Type == searchFilter.IssueType)
            .Where(x => string.IsNullOrEmpty(searchFilter.CustomFieldKey)
                || x.CustomFields.Any(value => value.FieldKey == searchFilter.CustomFieldKey
                    && value.SearchValue == searchFilter.CustomFieldValue))
            .Where(x => string.IsNullOrEmpty(text)
                || x.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.Labels.Any(label => label.Contains(text, StringComparison.OrdinalIgnoreCase))
                || x.CustomFields.Any(value => value.Indexed
                    && value.SearchValue.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Title, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
        var result = matches.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        await HydrateAllAsync(result, ct);
        return new WorkItemSearchPageResponse(
            result.Select(WorkItemResponseMapper.ToResponse).ToList(), matches.Count, true);
    }

    private async Task<ProjectResourceAuthorization> EnsurePermissionAsync(
        string projectId,
        string permission,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var authorization = await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
        authorizedOrganizationIds[projectId] = authorization.OrganizationId;
        return authorization;
    }

    private string CurrentOrganizationId(string projectId) =>
        authorizedOrganizationIds.TryGetValue(projectId, out var organizationId)
            ? organizationId
            : throw new InvalidOperationException("Project resource must be authorized before tenant data is accessed.");

    private async Task HydrateAllAsync(IEnumerable<WorkItemDocument> source, CancellationToken ct)
    {
        foreach (var workItem in source)
        {
            await activityStore.HydrateAsync(workItem, CurrentOrganizationId(workItem.ProjectId), ct);
        }
    }
}
