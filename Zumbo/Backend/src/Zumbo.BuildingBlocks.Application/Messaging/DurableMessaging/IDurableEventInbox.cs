namespace Zumbo.BuildingBlocks.Application.Messaging;

public interface IDurableEventInbox
{
    Task<bool> HasProcessedAsync(
        string consumerName,
        string messageId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkProcessedAsync(
        string consumerName,
        string messageId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default);
}
