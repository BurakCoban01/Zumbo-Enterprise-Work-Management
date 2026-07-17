namespace Zumbo.BuildingBlocks.Application.Runtime;

public enum ExternalDependencyOperationKind
{
    Read,
    IdempotentWrite,
    NonIdempotentWrite,
    Health
}

public sealed class ExternalDependencyPolicyOptions
{
    public int TimeoutMilliseconds { get; init; } = 5_000;
    public int MaxRetryAttempts { get; init; } = 2;
    public int BaseDelayMilliseconds { get; init; } = 100;
    public int MaximumDelayMilliseconds { get; init; } = 2_000;
    public double RetryJitterRatio { get; init; } = 0.2;
    public int CircuitFailureThreshold { get; init; } = 5;
    public int CircuitBreakMilliseconds { get; init; } = 30_000;
    public int BulkheadLimit { get; init; } = 32;
    public int QueueLimit { get; init; } = 64;

    public void Validate(string dependency)
    {
        if (TimeoutMilliseconds is < 10 or > 600_000)
            throw new InvalidOperationException($"External dependency '{dependency}' timeout must be between 10 and 600000 milliseconds.");
        if (MaxRetryAttempts is < 0 or > 5)
            throw new InvalidOperationException($"External dependency '{dependency}' retries must be between 0 and 5.");
        if (BaseDelayMilliseconds is < 1 or > 60_000
            || MaximumDelayMilliseconds < BaseDelayMilliseconds
            || MaximumDelayMilliseconds > 300_000)
            throw new InvalidOperationException($"External dependency '{dependency}' retry delays are invalid.");
        if (RetryJitterRatio is < 0 or > 1)
            throw new InvalidOperationException($"External dependency '{dependency}' jitter ratio must be between 0 and 1.");
        if (CircuitFailureThreshold is < 1 or > 100 || CircuitBreakMilliseconds is < 10 or > 600_000)
            throw new InvalidOperationException($"External dependency '{dependency}' circuit settings are invalid.");
        if (BulkheadLimit is < 1 or > 10_000 || QueueLimit is < 0 or > 100_000)
            throw new InvalidOperationException($"External dependency '{dependency}' bulkhead settings are invalid.");
    }
}

public sealed record ExternalDependencySnapshot(
    string Dependency,
    long Executions,
    long Attempts,
    long Retries,
    long Succeeded,
    long Failed,
    long TimedOut,
    long Rejected,
    long Cancelled,
    int InFlight,
    int Queued,
    bool CircuitOpen,
    double AverageLatencyMilliseconds);

public interface IExternalDependencyPolicy
{
    Task<T> ExecuteAsync<T>(
        string operation,
        ExternalDependencyOperationKind operationKind,
        Func<CancellationToken, Task<T>> action,
        Func<Exception, bool>? isTransient = null,
        CancellationToken cancellationToken = default);

    Task ExecuteAsync(
        string operation,
        ExternalDependencyOperationKind operationKind,
        Func<CancellationToken, Task> action,
        Func<Exception, bool>? isTransient = null,
        CancellationToken cancellationToken = default);
}

public interface IExternalDependencyPolicyProvider
{
    IExternalDependencyPolicy Get(string dependency);
    IReadOnlyList<ExternalDependencySnapshot> GetSnapshots();
}

public sealed class ExternalDependencyTransientException(string safeReason, Exception? innerException = null)
    : Exception(safeReason, innerException);

public sealed class ExternalDependencyTimeoutException(string dependency, string operation, Exception? innerException = null)
    : TimeoutException($"External dependency '{dependency}' timed out during '{operation}'.", innerException);

public sealed class ExternalDependencyCircuitOpenException(string dependency)
    : InvalidOperationException($"External dependency '{dependency}' circuit is open.");

public sealed class ExternalDependencyBulkheadRejectedException(string dependency)
    : InvalidOperationException($"External dependency '{dependency}' bulkhead is saturated.");

public static class ExternalDependencyNames
{
    public const string MongoDb = "mongodb";
    public const string PostgreSql = "postgresql";
    public const string Redis = "redis";
    public const string Minio = "minio";
    public const string OpenSearch = "opensearch";
    public const string Smtp = "smtp";
    public const string Webhook = "webhook";

    public static IReadOnlyList<string> All { get; } =
    [
        MongoDb,
        PostgreSql,
        Redis,
        Minio,
        OpenSearch,
        Smtp,
        Webhook
    ];
}
