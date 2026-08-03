using System.Collections.Concurrent;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class InMemoryDurableEventInbox : IDurableEventInbox
{
    private readonly ConcurrentDictionary<InboxIdentity, DateTimeOffset> _processed = new();

    public Task<bool> HasProcessedAsync(
        string consumerName,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var identity = Identity(consumerName, messageId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_processed.ContainsKey(identity));
    }

    public Task<bool> MarkProcessedAsync(
        string consumerName,
        string messageId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var identity = Identity(consumerName, messageId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_processed.TryAdd(identity, processedAtUtc));
    }

    private static InboxIdentity Identity(string consumerName, string messageId)
    {
        if (string.IsNullOrWhiteSpace(consumerName) || string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Inbox consumer and message identifiers are required.");
        }

        return new InboxIdentity(consumerName.Trim(), messageId.Trim());
    }

    private readonly record struct InboxIdentity(string ConsumerName, string MessageId);
}
