using Zumbo.Modules.WorkItems;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal static class WorkItemTemplateResponseMapper
{
    internal static WorkItemTemplateResponse ToResponse(WorkItemTemplateDocument template) => new(
        template.Id,
        template.ProjectId,
        template.BoardId,
        template.Name,
        template.Title,
        template.Description,
        template.Type,
        template.Priority,
        template.AssigneeUserId,
        template.TeamId,
        template.DueAfterDays,
        template.Labels,
        template.IssueTypeSchemaVersion,
        template.CustomFields.Select(value => new WorkItemCustomFieldValueResponse(
            value.FieldKey,
            value.Type,
            value.TextValue,
            value.NumberValue,
            value.BooleanValue,
            value.DateValueUtc is null
                ? null
                : DateOnly.FromDateTime(value.DateValueUtc.Value.UtcDateTime),
            value.OptionKey)).ToList(),
        template.Archived,
        template.Version);
}
