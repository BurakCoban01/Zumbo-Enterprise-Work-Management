using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.Modules.WorkItems;

public interface IWorkItemNotificationPublisher
{
    Task NotifyAsync(
        string userId,
        string type,
        string message,
        CancellationToken ct,
        string? deduplicationKey = null);

    Task NotifyWithSourceAsync(
        string userId,
        string type,
        string message,
        CancellationToken ct,
        string? deduplicationKey = null,
        string? sourceId = null,
        string? projectId = null);
}
