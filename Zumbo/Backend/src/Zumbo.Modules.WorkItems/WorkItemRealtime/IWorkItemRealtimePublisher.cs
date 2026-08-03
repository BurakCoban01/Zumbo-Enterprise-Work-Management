namespace Zumbo.Modules.WorkItems;

public interface IWorkItemRealtimePublisher
{
    Task PublishAsync(WorkItemRealtimeChange change, CancellationToken ct);
}
