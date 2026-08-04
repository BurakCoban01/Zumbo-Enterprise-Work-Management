using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    private WorkItemSearchRecord ToScopedSearchRecord(WorkItemDocument item) =>
        ToSearchRecord(item, CurrentOrganizationId(item.ProjectId));

    public static WorkItemSearchRecord ToSearchRecord(WorkItemDocument item, string organizationId = "") =>
        new(
            item.Id,
            item.ProjectId,
            item.BoardId,
            item.Title,
            item.Description,
            item.Status,
            item.Priority,
            item.AssigneeUserId,
            item.Labels,
            item.Type,
            string.Join(' ', item.CustomFields.Where(value => value.Indexed).Select(value => value.SearchValue)),
            item.CustomFields
                .Select(value => $"{value.FieldKey}\u001f{value.SearchValue}")
                .ToList(),
            organizationId);

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
                cursor,
                Math.Min(200, maximum - candidates.Count),
                ct);
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
        return new WorkItemSearchPageResponse(result.Select(ToResponse).ToList(), matches.Count, true);
    }
}
