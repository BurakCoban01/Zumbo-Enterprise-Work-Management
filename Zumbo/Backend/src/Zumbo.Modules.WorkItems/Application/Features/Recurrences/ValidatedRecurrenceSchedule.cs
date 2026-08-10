namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed record ValidatedRecurrenceSchedule(
    string Frequency,
    int Interval,
    DateTimeOffset StartAtUtc,
    DateTimeOffset? EndAtUtc,
    int MaxOccurrences);
