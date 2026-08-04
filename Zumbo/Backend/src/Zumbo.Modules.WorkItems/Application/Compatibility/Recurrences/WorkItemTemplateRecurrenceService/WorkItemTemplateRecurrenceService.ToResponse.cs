using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService{

    private static WorkItemTemplateResponse ToResponse(WorkItemTemplateDocument template) => new(
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
            value.DateValueUtc is null ? null : DateOnly.FromDateTime(value.DateValueUtc.Value.UtcDateTime),
            value.OptionKey)).ToList(),
        template.Archived,
        template.Version);

    private static WorkItemRecurrenceOccurrenceResponse ToResponse(
        WorkItemRecurrenceOccurrenceDocument occurrence) => new(
        occurrence.Id,
        occurrence.ScheduledForUtc,
        occurrence.Status,
        occurrence.CreatedWorkItemId,
        occurrence.GeneratedAt,
        occurrence.Version);
}
