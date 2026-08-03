using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

internal static class WorkItemPublicationMapper
{
    internal static WorkItemSearchRecord ToSearchRecord(WorkItemDocument item, string organizationId) =>
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
            item.CustomFields.Select(value => $"{value.FieldKey}\u001f{value.SearchValue}").ToList(),
            organizationId);

    internal static WorkItemRealtimeItem ToRealtimeItem(WorkItemDocument item) =>
        new(
            item.Id,
            item.ProjectId,
            item.BoardId,
            item.ColumnId,
            item.Title,
            item.Type,
            item.Priority,
            item.Status,
            item.AssigneeUserId,
            item.DueDate,
            item.SprintId,
            item.EstimatePoints,
            item.CompletedAt,
            item.Rank,
            item.Version);
}
