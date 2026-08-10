using Microsoft.Extensions.Options;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class RecurrenceSchedulePolicy(
    IOptions<WorkItemRecurrenceOptions> configuredOptions,
    IClock clock)
{
    internal ValidatedRecurrenceSchedule Validate(CreateWorkItemRecurrenceRequest request)
    {
        var frequency = WorkItemRecurrenceFrequencies.Normalize(request.Frequency);
        if (request.Interval is < 1 or > 365)
            throw new ValidationException("Recurrence interval must be between 1 and 365.");

        var maximumOccurrences = Math.Clamp(
            configuredOptions.Value.MaximumOccurrences,
            1,
            10_000);
        if (request.MaxOccurrences is < 1 || request.MaxOccurrences > maximumOccurrences)
        {
            throw new ValidationException(
                $"Recurrence maximum occurrences must be between 1 and {maximumOccurrences}.");
        }

        var start = request.StartAtUtc.ToUniversalTime();
        var end = request.EndAtUtc?.ToUniversalTime();
        var now = clock.UtcNow;
        var maxYears = Math.Clamp(configuredOptions.Value.MaximumScheduleYears, 1, 20);
        if (start < now.AddDays(-1) || start > now.AddYears(maxYears))
        {
            throw new ValidationException(
                $"Recurrence start must be within one day in the past and {maxYears} years in the future.");
        }
        if (end is not null && (end < start || end > start.AddYears(maxYears)))
        {
            throw new ValidationException(
                $"Recurrence end must follow the start and stay within {maxYears} years.");
        }

        return new ValidatedRecurrenceSchedule(
            frequency,
            request.Interval,
            start,
            end,
            request.MaxOccurrences);
    }

    internal static DateTimeOffset Next(
        DateTimeOffset value,
        string frequency,
        int interval) =>
        frequency switch
        {
            WorkItemRecurrenceFrequencies.Daily => value.AddDays(interval),
            WorkItemRecurrenceFrequencies.Weekly => value.AddDays(checked(interval * 7)),
            WorkItemRecurrenceFrequencies.Monthly => value.AddMonths(interval),
            _ => throw new InvalidOperationException("Stored recurrence frequency is invalid.")
        };
}
