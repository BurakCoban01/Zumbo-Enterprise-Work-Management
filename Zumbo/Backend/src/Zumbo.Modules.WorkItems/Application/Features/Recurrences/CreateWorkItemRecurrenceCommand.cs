namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed record CreateWorkItemRecurrenceCommand(
    CreateWorkItemRecurrenceRequest Request,
    string CorrelationId);
