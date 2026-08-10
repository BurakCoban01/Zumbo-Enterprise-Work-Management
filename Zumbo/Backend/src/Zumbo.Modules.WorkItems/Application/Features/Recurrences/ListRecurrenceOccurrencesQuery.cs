namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed record ListRecurrenceOccurrencesQuery(
    string RecurrenceId,
    int Page,
    int PageSize);
