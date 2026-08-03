namespace Zumbo.BuildingBlocks.Application.Messaging;

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

    Task<IReadOnlyList<DurableDeadLetterSummary>> ListDeadLettersAsync(
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<DurableOutboxMetrics> GetMetricsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}
