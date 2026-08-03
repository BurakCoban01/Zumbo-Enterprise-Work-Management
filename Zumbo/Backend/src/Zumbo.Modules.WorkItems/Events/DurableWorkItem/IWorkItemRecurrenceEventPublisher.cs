using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public interface IWorkItemRecurrenceEventPublisher
{
    Task PublishAsync(WorkItemRecurrenceDueEvent message, CancellationToken ct);
}
