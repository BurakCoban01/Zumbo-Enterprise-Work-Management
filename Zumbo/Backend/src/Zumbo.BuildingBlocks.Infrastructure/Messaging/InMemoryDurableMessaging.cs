using System.Collections.Concurrent;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class InMemoryDurableEventOutbox : IDurableEventOutbox
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, OutboxEntry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<DeduplicationIdentity> _deduplicationKeys = [];

    public Task EnqueueAsync(
        DurableEventEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_entries.ContainsKey(message.Id))
            {
                return Task.CompletedTask;
            }

            if (message.DeduplicationKey is not null)
            {
                var identity = new DeduplicationIdentity(
                    message.OwnerModule,
                    message.EventType,
                    message.DeduplicationKey);
                if (!_deduplicationKeys.Add(identity))
                {
                    return Task.CompletedTask;
                }
            }

            _entries.Add(message.Id, new OutboxEntry(message));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DurableEventLease>> ClaimAsync(
        string workerId,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkerId = Required(workerId);
        if (batchSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var candidates = _entries.Values
                .Where(entry =>
                    (entry.State == DurableMessageStates.Pending && entry.AvailableAtUtc <= nowUtc)
                    || (entry.State == DurableMessageStates.Processing && entry.LeaseUntilUtc <= nowUtc))
                .OrderBy(entry => entry.Message.OccurredAtUtc)
                .ThenBy(entry => entry.Message.Id, StringComparer.Ordinal)
                .Take(batchSize)
                .ToList();
            var leases = new List<DurableEventLease>(candidates.Count);
            foreach (var entry in candidates)
            {
                entry.State = DurableMessageStates.Processing;
                entry.Attempt = checked(entry.Attempt + 1);
                entry.WorkerId = normalizedWorkerId;
                entry.LeaseToken = Guid.NewGuid().ToString("N");
                entry.LeaseUntilUtc = nowUtc.Add(leaseDuration);
                leases.Add(new DurableEventLease(
                    entry.Message,
                    entry.Attempt,
                    normalizedWorkerId,
                    entry.LeaseToken,
                    entry.LeaseUntilUtc.Value));
            }

            return Task.FromResult<IReadOnlyList<DurableEventLease>>(leases);
        }
    }

    public Task<bool> CompleteAsync(
        string messageId,
        string leaseToken,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedMessageId = Required(messageId);
        var normalizedLeaseToken = Required(leaseToken);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(normalizedMessageId, out var entry)
                || entry.State != DurableMessageStates.Processing
                || !string.Equals(entry.LeaseToken, normalizedLeaseToken, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            entry.State = DurableMessageStates.Completed;
            entry.CompletedAtUtc = completedAtUtc;
            entry.LastError = null;
            ClearLease(entry);
            return Task.FromResult(true);
        }
    }

    public Task<DurableMessageFailure> FailAsync(
        string messageId,
        string leaseToken,
        string error,
        int maximumAttempts,
        DateTimeOffset nowUtc,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedMessageId = Required(messageId);
        var normalizedLeaseToken = Required(leaseToken);
        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(normalizedMessageId, out var entry)
                || entry.State != DurableMessageStates.Processing
                || !string.Equals(entry.LeaseToken, normalizedLeaseToken, StringComparison.Ordinal))
            {
                return Task.FromResult(new DurableMessageFailure(false, false, 0, null));
            }

            entry.LastError = string.IsNullOrWhiteSpace(error)
                ? "Unknown durable event failure."
                : error.Trim();
            var deadLettered = entry.Attempt >= maximumAttempts;
            entry.State = deadLettered
                ? DurableMessageStates.DeadLetter
                : DurableMessageStates.Pending;
            entry.AvailableAtUtc = deadLettered ? entry.AvailableAtUtc : nextAttemptAtUtc;
            entry.DeadLetteredAtUtc = deadLettered ? nowUtc : null;
            ClearLease(entry);
            return Task.FromResult(new DurableMessageFailure(
                true,
                deadLettered,
                entry.Attempt,
                deadLettered ? null : nextAttemptAtUtc));
        }
    }

    public Task<bool> ReplayDeadLetterAsync(
        string messageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedMessageId = Required(messageId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(normalizedMessageId, out var entry)
                || entry.State != DurableMessageStates.DeadLetter)
            {
                return Task.FromResult(false);
            }

            entry.State = DurableMessageStates.Pending;
            entry.Attempt = 0;
            entry.AvailableAtUtc = nowUtc;
            entry.CompletedAtUtc = null;
            entry.DeadLetteredAtUtc = null;
            entry.LastError = null;
            ClearLease(entry);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<DurableDeadLetterSummary>> ListDeadLettersAsync(
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var items = _entries.Values
                .Where(entry => entry.State == DurableMessageStates.DeadLetter)
                .OrderByDescending(entry => entry.DeadLetteredAtUtc)
                .ThenBy(entry => entry.Message.Id, StringComparer.Ordinal)
                .Take(pageSize)
                .Select(entry => new DurableDeadLetterSummary(
                    entry.Message.Id,
                    entry.Message.EventType,
                    entry.Attempt,
                    entry.DeadLetteredAtUtc ?? entry.Message.OccurredAtUtc))
                .ToList();
            return Task.FromResult<IReadOnlyList<DurableDeadLetterSummary>>(items);
        }
    }

    public Task<DurableOutboxMetrics> GetMetricsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var entries = _entries.Values.ToList();
            var metrics = new DurableOutboxMetrics(
                entries.LongCount(entry => entry.State == DurableMessageStates.Pending),
                entries.LongCount(entry => entry.State == DurableMessageStates.Processing),
                entries.LongCount(entry => entry.State == DurableMessageStates.DeadLetter),
                entries.LongCount(entry => entry.State == DurableMessageStates.Completed),
                entries.LongCount(entry => entry.Attempt > 1),
                entries
                    .Where(entry => entry.State == DurableMessageStates.Pending)
                    .Select(entry => (DateTimeOffset?)entry.Message.OccurredAtUtc)
                    .Min(),
                nowUtc);
            return Task.FromResult(metrics);
        }
    }

    private static void ClearLease(OutboxEntry entry)
    {
        entry.WorkerId = null;
        entry.LeaseToken = null;
        entry.LeaseUntilUtc = null;
    }

    private static string Required(string value) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException("Value cannot be empty.");

    private sealed class OutboxEntry(DurableEventEnvelope message)
    {
        public DurableEventEnvelope Message { get; } = message;
        public string State { get; set; } = DurableMessageStates.Pending;
        public int Attempt { get; set; }
        public DateTimeOffset AvailableAtUtc { get; set; } = message.OccurredAtUtc;
        public string? WorkerId { get; set; }
        public string? LeaseToken { get; set; }
        public DateTimeOffset? LeaseUntilUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public DateTimeOffset? DeadLetteredAtUtc { get; set; }
        public string? LastError { get; set; }
    }

    private readonly record struct DeduplicationIdentity(
        string OwnerModule,
        string EventType,
        string DeduplicationKey);
}

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

public sealed class InMemoryDurableTransactionRunner : IDurableTransactionRunner
{
    public async Task ExecuteAsync(
        string ownerModule,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        await operation(cancellationToken);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        string ownerModule,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        return await operation(cancellationToken);
    }
}

public sealed class RandomDurableMessageJitter : IDurableMessageJitter
{
    public double NextUnit() => Random.Shared.NextDouble();
}
