namespace Zumbo.BuildingBlocks.Application.Messaging;

public static class DurableMessageStates
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string DeadLetter = "DeadLetter";
}

public sealed record DurableEventEnvelope(
    string Id,
    string OwnerModule,
    string EventType,
    int SchemaVersion,
    string TenantId,
    string CorrelationId,
    string? DeduplicationKey,
    string Payload,
    DateTimeOffset OccurredAtUtc)
{
    public static DurableEventEnvelope Create(
        string ownerModule,
        string eventType,
        int schemaVersion,
        string tenantId,
        string correlationId,
        string payload,
        DateTimeOffset occurredAtUtc,
        string? deduplicationKey = null) =>
        new(
            Guid.NewGuid().ToString("N"),
            Required(ownerModule, nameof(ownerModule)),
            Required(eventType, nameof(eventType)),
            schemaVersion > 0 ? schemaVersion : throw new ArgumentOutOfRangeException(nameof(schemaVersion)),
            Required(tenantId, nameof(tenantId)),
            Required(correlationId, nameof(correlationId)),
            string.IsNullOrWhiteSpace(deduplicationKey) ? null : deduplicationKey.Trim(),
            Required(payload, nameof(payload)),
            occurredAtUtc);

    private static string Required(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException("A durable event value cannot be empty.", parameterName);
}

public sealed record DurableEventLease(
    DurableEventEnvelope Event,
    int Attempt,
    string WorkerId,
    string LeaseToken,
    DateTimeOffset LeaseUntilUtc);

public sealed record DurableOutboxMetrics(
    long Pending,
    long Processing,
    long DeadLetter,
    long Completed,
    long Retried,
    DateTimeOffset? OldestPendingAtUtc,
    DateTimeOffset CapturedAtUtc);

public sealed record DurableMessageFailure(
    bool Updated,
    bool DeadLettered,
    int Attempt,
    DateTimeOffset? NextAttemptAtUtc);

public interface IDurableEventOutbox
{
    Task EnqueueAsync(DurableEventEnvelope message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DurableEventLease>> ClaimAsync(
        string workerId,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(
        string messageId,
        string leaseToken,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);

    Task<DurableMessageFailure> FailAsync(
        string messageId,
        string leaseToken,
        string error,
        int maximumAttempts,
        DateTimeOffset nowUtc,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> ReplayDeadLetterAsync(
        string messageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<DurableOutboxMetrics> GetMetricsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

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

public interface IDurableEventHandler
{
    string ConsumerName { get; }
    string EventType { get; }
    Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken);
}

public interface IDurableTransactionRunner
{
    Task ExecuteAsync(
        string ownerModule,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteAsync<TResult>(
        string ownerModule,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}

public interface IDurableMessageJitter
{
    double NextUnit();
}

public sealed class DurableMessageRetryPolicy(
    TimeSpan baseDelay,
    TimeSpan maximumDelay,
    double jitterRatio,
    IDurableMessageJitter jitter)
{
    public TimeSpan DelayForAttempt(int attempt)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        if (baseDelay <= TimeSpan.Zero || maximumDelay < baseDelay || jitterRatio is < 0 or > 1)
        {
            throw new InvalidOperationException("Durable message retry settings are invalid.");
        }

        var exponent = Math.Min(attempt - 1, 30);
        var exponentialMilliseconds = Math.Min(
            baseDelay.TotalMilliseconds * Math.Pow(2, exponent),
            maximumDelay.TotalMilliseconds);
        var centeredJitter = ((jitter.NextUnit() * 2) - 1) * jitterRatio;
        var milliseconds = Math.Clamp(
            exponentialMilliseconds * (1 + centeredJitter),
            baseDelay.TotalMilliseconds * (1 - jitterRatio),
            maximumDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
