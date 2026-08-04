using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public interface IWorkItemAutomationEventPublisher
{
    Task PublishAsync(WorkItemAutomationEvent message, CancellationToken ct);
}
