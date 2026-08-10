namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed record ArchiveWorkItemRecurrenceCommand(
    string RecurrenceId,
    string CorrelationId);
