namespace Zumbo.BuildingBlocks.Application.Runtime;

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
