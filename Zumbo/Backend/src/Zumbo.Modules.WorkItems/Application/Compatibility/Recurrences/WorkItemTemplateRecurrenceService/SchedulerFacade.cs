using Zumbo.Modules.WorkItems.Application.Features.Recurrences;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService
{
    public async Task<int> ScheduleDueAsync(CancellationToken ct) =>
        await scheduleDueRecurrencesHandler.HandleAsync(new ScheduleDueRecurrencesCommand(), ct);

    public static string StableOccurrenceId(string recurrenceId, DateTimeOffset scheduledForUtc) =>
        RecurrenceOccurrenceIdentity.Create(recurrenceId, scheduledForUtc);
}
