namespace Zumbo.Modules.WorkItems;

public interface IWorkItemBulkJobEventPublisher
{
    Task PublishAsync(WorkItemBulkJobDueEvent message, CancellationToken ct);
}
