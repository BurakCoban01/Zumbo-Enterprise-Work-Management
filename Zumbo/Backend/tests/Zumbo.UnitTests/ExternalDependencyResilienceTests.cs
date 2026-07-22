using Microsoft.Extensions.Logging.Abstractions;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Runtime;

namespace Zumbo.UnitTests;

public sealed class ExternalDependencyResilienceTests
{
    [Fact]
    public async Task IdempotentOperation_RetriesTransientFailureWithBoundedJitter()
    {
        using var telemetry = new ExternalDependencyTelemetry();
        using var policy = Policy(telemetry, maxRetryAttempts: 2);
        var attempts = 0;

        var result = await policy.ExecuteAsync(
            "read",
            ExternalDependencyOperationKind.Read,
            _ => ++attempts < 3
                ? Task.FromException<string>(new ExternalDependencyTransientException("temporary"))
                : Task.FromResult("ok"));

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
        var snapshot = Assert.Single(telemetry.GetSnapshots());
        Assert.Equal(2, snapshot.Retries);
        Assert.Equal(1, snapshot.Succeeded);
        Assert.Equal(0, snapshot.Failed);
    }

    [Fact]
    public async Task NonIdempotentOperation_NeverReplaysTransientFailure()
    {
        using var telemetry = new ExternalDependencyTelemetry();
        using var policy = Policy(telemetry, maxRetryAttempts: 3);
        var attempts = 0;

        await Assert.ThrowsAsync<ExternalDependencyTransientException>(() => policy.ExecuteAsync(
            "send",
            ExternalDependencyOperationKind.NonIdempotentWrite,
            _ => { attempts++; return Task.FromException(new ExternalDependencyTransientException("unknown outcome")); }));

        Assert.Equal(1, attempts);
        Assert.Equal(0, Assert.Single(telemetry.GetSnapshots()).Retries);
    }

    [Fact]
    public async Task Timeout_IsBoundedAndRecorded()
    {
        using var telemetry = new ExternalDependencyTelemetry();
        using var policy = Policy(telemetry, timeoutMilliseconds: 20);

        await Assert.ThrowsAsync<ExternalDependencyTimeoutException>(() => policy.ExecuteAsync(
            "slow-read",
            ExternalDependencyOperationKind.Read,
            token => Task.Delay(TimeSpan.FromSeconds(5), token)));

        var snapshot = Assert.Single(telemetry.GetSnapshots());
        Assert.Equal(1, snapshot.TimedOut);
        Assert.Equal(0, snapshot.Cancelled);
    }

    [Fact]
    public async Task CallerCancellation_IsNotRetriedOrCountedAsDependencyFailure()
    {
        using var telemetry = new ExternalDependencyTelemetry();
        using var policy = Policy(telemetry, maxRetryAttempts: 3);
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = policy.ExecuteAsync(
            "cancelled",
            ExternalDependencyOperationKind.Read,
            async token => { entered.SetResult(); await Task.Delay(10_000, token); },
            cancellationToken: cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);

        var snapshot = Assert.Single(telemetry.GetSnapshots());
        Assert.Equal(1, snapshot.Cancelled);
        Assert.Equal(0, snapshot.Retries);
        Assert.False(snapshot.CircuitOpen);
    }

    [Fact]
    public async Task Circuit_OpensAfterThresholdAndRejectsWithoutCallingDependency()
    {
        using var telemetry = new ExternalDependencyTelemetry();
        using var policy = Policy(telemetry, circuitFailureThreshold: 2);
        var attempts = 0;
        Task Failure() => policy.ExecuteAsync(
            "read",
            ExternalDependencyOperationKind.Read,
            _ => { attempts++; return Task.FromException(new ExternalDependencyTransientException("down")); });

        await Assert.ThrowsAsync<ExternalDependencyTransientException>(Failure);
        await Assert.ThrowsAsync<ExternalDependencyTransientException>(Failure);
        await Assert.ThrowsAsync<ExternalDependencyCircuitOpenException>(Failure);

        Assert.Equal(2, attempts);
        var snapshot = Assert.Single(telemetry.GetSnapshots());
        Assert.True(snapshot.CircuitOpen);
        Assert.Equal(1, snapshot.Rejected);
    }

    [Fact]
    public async Task Circuit_HalfOpenProbeRecoversAfterBreakWindow()
    {
        using var telemetry = new ExternalDependencyTelemetry();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero));
        using var policy = new ExternalDependencyPolicy(
            ExternalDependencyNames.OpenSearch,
            new ExternalDependencyPolicyOptions
            {
                TimeoutMilliseconds = 1_000,
                MaxRetryAttempts = 0,
                BaseDelayMilliseconds = 1,
                MaximumDelayMilliseconds = 1,
                CircuitFailureThreshold = 1,
                CircuitBreakMilliseconds = 10,
                BulkheadLimit = 1,
                QueueLimit = 0
            },
            telemetry,
            new FixedJitter(),
            NullLogger.Instance,
            time);

        await Assert.ThrowsAsync<ExternalDependencyTransientException>(() => policy.ExecuteAsync(
            "read",
            ExternalDependencyOperationKind.Read,
            _ => Task.FromException(new ExternalDependencyTransientException("down"))));
        await Assert.ThrowsAsync<ExternalDependencyCircuitOpenException>(() => policy.ExecuteAsync(
            "read",
            ExternalDependencyOperationKind.Read,
            _ => Task.CompletedTask));

        time.Advance(TimeSpan.FromMilliseconds(11));
        await policy.ExecuteAsync(
            "read",
            ExternalDependencyOperationKind.Read,
            _ => Task.CompletedTask);

        Assert.False(Assert.Single(telemetry.GetSnapshots()).CircuitOpen);
    }

    [Fact]
    public async Task Bulkhead_RejectsWhenExecutionAndQueueAreFull()
    {
        using var telemetry = new ExternalDependencyTelemetry();
        using var policy = Policy(telemetry, bulkheadLimit: 1, queueLimit: 0);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = policy.ExecuteAsync(
            "hold",
            ExternalDependencyOperationKind.Read,
            async _ => { entered.SetResult(); await release.Task; });
        await entered.Task;

        await Assert.ThrowsAsync<ExternalDependencyBulkheadRejectedException>(() => policy.ExecuteAsync(
            "rejected",
            ExternalDependencyOperationKind.Read,
            _ => Task.CompletedTask));
        release.SetResult();
        await first;

        Assert.Equal(1, Assert.Single(telemetry.GetSnapshots()).Rejected);
    }

    [Fact]
    public async Task CancellationDuringRetryDelay_CompletesTelemetryAndReleasesExecution()
    {
        using var telemetry = new ExternalDependencyTelemetry();
        using var policy = new ExternalDependencyPolicy(
            ExternalDependencyNames.OpenSearch,
            new ExternalDependencyPolicyOptions
            {
                TimeoutMilliseconds = 1_000,
                MaxRetryAttempts = 2,
                BaseDelayMilliseconds = 5_000,
                MaximumDelayMilliseconds = 5_000,
                RetryJitterRatio = 0,
                CircuitFailureThreshold = 2,
                CircuitBreakMilliseconds = 1_000,
                BulkheadLimit = 1,
                QueueLimit = 1
            },
            telemetry,
            new FixedJitter(),
            NullLogger.Instance);
        using var cancellation = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => policy.ExecuteAsync(
            "read",
            ExternalDependencyOperationKind.Read,
            _ => Task.FromException<int>(new ExternalDependencyTransientException("temporary")),
            cancellationToken: cancellation.Token));

        var snapshot = Assert.Single(telemetry.GetSnapshots());
        Assert.Equal(1, snapshot.Executions);
        Assert.Equal(0, snapshot.InFlight);
        Assert.Equal(1, snapshot.Cancelled);
        Assert.False(snapshot.CircuitOpen);
    }

    [Fact]
    public async Task Timeout_IsEnforcedWhenAdapterTaskIgnoresCancellation()
    {
        using var telemetry = new ExternalDependencyTelemetry();
        using var policy = Policy(telemetry, timeoutMilliseconds: 50);
        var neverCompletes = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await Assert.ThrowsAsync<ExternalDependencyTimeoutException>(() => policy.ExecuteAsync(
            "read",
            ExternalDependencyOperationKind.Read,
            _ => neverCompletes.Task));

        var snapshot = Assert.Single(telemetry.GetSnapshots());
        Assert.Equal(1, snapshot.TimedOut);
        Assert.Equal(0, snapshot.InFlight);
    }

    [Fact]
    public void Catalog_ContainsEveryRequiredExternalAdapter()
    {
        Assert.Equal(
            ["mongodb", "postgresql", "redis", "minio", "opensearch", "smtp", "webhook"],
            ExternalDependencyNames.All);
    }

    [Fact]
    public void InvalidPolicyBounds_AreRejectedAtConstruction()
    {
        using var telemetry = new ExternalDependencyTelemetry();
        Assert.Throws<InvalidOperationException>(() => new ExternalDependencyPolicy(
            "smtp",
            new ExternalDependencyPolicyOptions { TimeoutMilliseconds = 1 },
            telemetry,
            new FixedJitter(),
            NullLogger.Instance));
    }

    private static ExternalDependencyPolicy Policy(
        ExternalDependencyTelemetry telemetry,
        int timeoutMilliseconds = 1_000,
        int maxRetryAttempts = 0,
        int circuitFailureThreshold = 5,
        int bulkheadLimit = 4,
        int queueLimit = 4) =>
        new(
            "opensearch",
            new ExternalDependencyPolicyOptions
            {
                TimeoutMilliseconds = timeoutMilliseconds,
                MaxRetryAttempts = maxRetryAttempts,
                BaseDelayMilliseconds = 1,
                MaximumDelayMilliseconds = 2,
                RetryJitterRatio = 0.5,
                CircuitFailureThreshold = circuitFailureThreshold,
                CircuitBreakMilliseconds = 10_000,
                BulkheadLimit = bulkheadLimit,
                QueueLimit = queueLimit
            },
            telemetry,
            new FixedJitter(),
            NullLogger.Instance);

    private sealed class FixedJitter : IExternalDependencyJitter
    {
        public TimeSpan Apply(TimeSpan delay, double ratio) => delay;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
