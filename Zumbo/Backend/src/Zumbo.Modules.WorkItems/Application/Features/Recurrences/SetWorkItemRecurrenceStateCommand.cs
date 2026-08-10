namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed record SetWorkItemRecurrenceStateCommand(
    string RecurrenceId,
    bool Active,
    string CorrelationId);
