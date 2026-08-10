using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService
{
    private async Task<WorkItemRecurrenceDocument> GetRecurrenceAsync(
        string recurrenceId,
        bool includeArchived,
        CancellationToken ct) =>
        await recurrences.SelectAsync(
            recurrence => recurrence.Id == recurrenceId && (includeArchived || !recurrence.Archived), ct)
        ?? throw new NotFoundException("WORK_ITEM_RECURRENCE_NOT_FOUND", "Work item recurrence was not found.");

    private static DateTimeOffset Next(DateTimeOffset value, string frequency, int interval) =>
        frequency switch
        {
            WorkItemRecurrenceFrequencies.Daily => value.AddDays(interval),
            WorkItemRecurrenceFrequencies.Weekly => value.AddDays(checked(interval * 7)),
            WorkItemRecurrenceFrequencies.Monthly => value.AddMonths(interval),
            _ => throw new InvalidOperationException("Stored recurrence frequency is invalid.")
        };

    private async Task ReplaceRecurrenceAsync(WorkItemRecurrenceDocument recurrence, CancellationToken ct)
    {
        var result = await recurrences.ReplaceByVersionAsync(
            x => x.Id == recurrence.Id, recurrence, recurrence.Version, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_RECURRENCE_CONFLICT", "The recurrence changed concurrently; retry the operation.");
        }
        recurrence.Version = result.Version!.Value;
    }

    private sealed record Schedule(
        string Frequency,
        int Interval,
        DateTimeOffset StartAtUtc,
        DateTimeOffset? EndAtUtc,
        int MaxOccurrences);

    private async Task<WorkItemRecurrenceResponse> ToResponseAsync(
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

    private static WorkItemRecurrenceOccurrenceResponse ToResponse(
        WorkItemRecurrenceOccurrenceDocument occurrence) => new(
        occurrence.Id,
        occurrence.ScheduledForUtc,
        occurrence.Status,
        occurrence.CreatedWorkItemId,
        occurrence.GeneratedAt,
        occurrence.Version);

    private Schedule ValidateSchedule(CreateWorkItemRecurrenceRequest request)
    {
        var frequency = WorkItemRecurrenceFrequencies.Normalize(request.Frequency);
        if (request.Interval is < 1 or > 365)
        {
            throw new ValidationException("Recurrence interval must be between 1 and 365.");
        }
        var maximumOccurrences = Math.Clamp(Options.MaximumOccurrences, 1, 10_000);
        if (request.MaxOccurrences is < 1 || request.MaxOccurrences > maximumOccurrences)
        {
            throw new ValidationException($"Recurrence maximum occurrences must be between 1 and {maximumOccurrences}.");
        }

        var start = request.StartAtUtc.ToUniversalTime();
        var end = request.EndAtUtc?.ToUniversalTime();
        var now = clock.UtcNow;
        var maxYears = Math.Clamp(Options.MaximumScheduleYears, 1, 20);
        if (start < now.AddDays(-1) || start > now.AddYears(maxYears))
        {
            throw new ValidationException($"Recurrence start must be within one day in the past and {maxYears} years in the future.");
        }
        if (end is not null && (end < start || end > start.AddYears(maxYears)))
        {
            throw new ValidationException($"Recurrence end must follow the start and stay within {maxYears} years.");
        }
        return new Schedule(frequency, request.Interval, start, end, request.MaxOccurrences);
    }
}
