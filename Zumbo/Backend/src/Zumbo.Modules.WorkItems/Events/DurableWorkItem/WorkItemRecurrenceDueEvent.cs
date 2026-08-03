using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemRecurrenceDueEvent(
    string OrganizationId,
    string ProjectId,
    string RecurrenceId,
    string OccurrenceId,
    DateTimeOffset ScheduledForUtc);
