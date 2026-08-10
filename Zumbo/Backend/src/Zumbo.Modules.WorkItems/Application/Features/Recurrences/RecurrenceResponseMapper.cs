using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class RecurrenceResponseMapper(
    IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences)
{
    internal async Task<WorkItemRecurrenceResponse> ToResponseAsync(
        WorkItemRecurrenceDocument recurrence,
        CancellationToken ct)
    {
        var generated = await occurrences.CountByFilterAsync(
            occurrence => occurrence.RecurrenceId == recurrence.Id
                && occurrence.Status == WorkItemRecurrenceOccurrenceStates.Generated,
            ct);
        return new WorkItemRecurrenceResponse(
            recurrence.Id,
            recurrence.ProjectId,
            recurrence.TemplateId,
            recurrence.Frequency,
            recurrence.Interval,
            recurrence.StartAtUtc,
            recurrence.EndAtUtc,
            recurrence.NextRunAtUtc,
            recurrence.MaxOccurrences,
            recurrence.ScheduledOccurrences,
            generated,
            recurrence.Active,
            recurrence.Archived,
            recurrence.Version);
    }

    internal static WorkItemRecurrenceOccurrenceResponse ToOccurrenceResponse(
        WorkItemRecurrenceOccurrenceDocument occurrence) => new(
        occurrence.Id,
        occurrence.ScheduledForUtc,
        occurrence.Status,
        occurrence.CreatedWorkItemId,
        occurrence.GeneratedAt,
        occurrence.Version);
}
