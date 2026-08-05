using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    public async Task<IReadOnlyList<WorkItemResponse>> SearchAsync(WorkItemSearchRequest request, CancellationToken ct) =>
        await searchWorkItemsHandler.HandleAsync(request, ct);

    public async Task<WorkItemSearchPageResponse> SearchPageAsync(WorkItemSearchRequest request, CancellationToken ct)
    {
        SearchWorkItemsValidator.Validate(request);
        await EnsurePermissionAsync(request.ProjectId!, "WorkItemView", ct);
        var searchFilter = await typeSchemas.ValidateSearchFilterAsync(
            request.ProjectId!,
            request.IssueType,
            request.CustomFieldKey,
            request.CustomFieldValue,
            ct);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var text = request.Text?.Trim().ToLowerInvariant();

        if (!request.Archived && (!string.IsNullOrWhiteSpace(text)
            || !string.IsNullOrWhiteSpace(searchFilter.IssueType)
            || !string.IsNullOrWhiteSpace(searchFilter.CustomFieldKey)))
        {
            WorkItemSearchResult searchResult;
            var query = new WorkItemSearchQuery(
                    CurrentOrganizationId(request.ProjectId!),
                    request.ProjectId!,
                    text,
                    request.AssigneeUserId,
                    request.Status,
                    page,
                    pageSize,
                    searchFilter.IssueType,
                    searchFilter.CustomFieldKey,
                    searchFilter.CustomFieldValue);
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
            var items = ids
                .Where(resultById.ContainsKey)
                .Select(id => ToResponse(resultById[id]))
                .ToList();
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
        return new WorkItemSearchPageResponse(result.Select(ToResponse).ToList(), totalCount, false);
    }

    public async Task<WorkItemResponse> GetAsync(string id, CancellationToken ct)
        => await getWorkItemHandler.HandleAsync(new GetWorkItemQuery(id), ct);
}
