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
