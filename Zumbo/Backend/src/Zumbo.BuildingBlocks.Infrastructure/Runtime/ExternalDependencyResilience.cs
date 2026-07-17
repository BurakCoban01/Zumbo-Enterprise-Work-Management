using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Runtime;

public interface IExternalDependencyJitter
{
    TimeSpan Apply(TimeSpan delay, double ratio);
}

public sealed class RandomExternalDependencyJitter : IExternalDependencyJitter
{
    public TimeSpan Apply(TimeSpan delay, double ratio)
    {
        var boundedRatio = Math.Clamp(ratio, 0, 1);
        var factor = 1 - boundedRatio + Random.Shared.NextDouble() * boundedRatio * 2;
        return TimeSpan.FromMilliseconds(Math.Max(1, delay.TotalMilliseconds * factor));
    }
}

public sealed class ExternalDependencyTelemetry : IDisposable
{
    private readonly ConcurrentDictionary<string, MutableSnapshot> snapshots = new(StringComparer.Ordinal);
    private readonly Meter meter = new("Zumbo.ExternalDependencies", "1.0.0");
    private readonly Counter<long> executionCounter;
    private readonly Counter<long> retryCounter;
    private readonly Counter<long> failureCounter;
    private readonly Counter<long> rejectionCounter;
    private readonly Histogram<double> latencyHistogram;

    public ExternalDependencyTelemetry()
    {
        executionCounter = meter.CreateCounter<long>("zumbo.external.executions", unit: "{execution}");
        retryCounter = meter.CreateCounter<long>("zumbo.external.retries", unit: "{retry}");
        failureCounter = meter.CreateCounter<long>("zumbo.external.failures", unit: "{failure}");
        rejectionCounter = meter.CreateCounter<long>("zumbo.external.rejections", unit: "{rejection}");
        latencyHistogram = meter.CreateHistogram<double>("zumbo.external.duration", unit: "ms");
    }

    internal void ExecutionStarted(string dependency, string operation)
    {
        var snapshot = Get(dependency);
        Interlocked.Increment(ref snapshot.Executions);
        Interlocked.Increment(ref snapshot.InFlight);
        executionCounter.Add(1, Tags(dependency, operation));
    }

    internal void Attempted(string dependency) => Interlocked.Increment(ref Get(dependency).Attempts);

    internal void Retried(string dependency, string operation)
    {
        Interlocked.Increment(ref Get(dependency).Retries);
        retryCounter.Add(1, Tags(dependency, operation));
    }

    internal void Queued(string dependency, int delta) => Interlocked.Add(ref Get(dependency).Queued, delta);

    internal void Rejected(string dependency, string operation, string reason)
    {
        Interlocked.Increment(ref Get(dependency).Rejected);
        rejectionCounter.Add(1, Tags(dependency, operation, reason));
    }

    internal void Completed(
        string dependency,
        string operation,
        long startedTimestamp,
        string outcome,
        bool circuitOpen)
    {
        var snapshot = Get(dependency);
        Interlocked.Decrement(ref snapshot.InFlight);
        switch (outcome)
        {
            case "success": Interlocked.Increment(ref snapshot.Succeeded); break;
            case "timeout": Interlocked.Increment(ref snapshot.TimedOut); break;
            case "cancelled": Interlocked.Increment(ref snapshot.Cancelled); break;
            default: Interlocked.Increment(ref snapshot.Failed); break;
        }

        Volatile.Write(ref snapshot.CircuitOpen, circuitOpen ? 1 : 0);
        var elapsed = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
        Interlocked.Add(ref snapshot.TotalDurationTicks, TimeSpan.FromMilliseconds(elapsed).Ticks);
        latencyHistogram.Record(elapsed, Tags(dependency, operation, outcome));
        if (outcome is not "success" and not "cancelled")
            failureCounter.Add(1, Tags(dependency, operation, outcome));
    }

    internal void SetCircuit(string dependency, bool open) =>
        Volatile.Write(ref Get(dependency).CircuitOpen, open ? 1 : 0);

    public IReadOnlyList<ExternalDependencySnapshot> GetSnapshots() => snapshots
        .Select(pair => pair.Value.ToSnapshot(pair.Key))
        .OrderBy(snapshot => snapshot.Dependency, StringComparer.Ordinal)
        .ToList();

    private MutableSnapshot Get(string dependency) => snapshots.GetOrAdd(dependency, static _ => new MutableSnapshot());

    private static TagList Tags(string dependency, string operation, string? outcome = null)
    {
        var tags = new TagList { { "dependency", dependency }, { "operation", operation } };
        if (outcome is not null) tags.Add("outcome", outcome);
        return tags;
    }

    public void Dispose() => meter.Dispose();

    private sealed class MutableSnapshot
    {
        public long Executions;
        public long Attempts;
        public long Retries;
        public long Succeeded;
        public long Failed;
        public long TimedOut;
        public long Rejected;
        public long Cancelled;
        public int InFlight;
        public int Queued;
        public int CircuitOpen;
        public long TotalDurationTicks;

        public ExternalDependencySnapshot ToSnapshot(string dependency)
        {
            var executions = Interlocked.Read(ref Executions);
            var ticks = Interlocked.Read(ref TotalDurationTicks);
            return new ExternalDependencySnapshot(
                dependency,
                executions,
                Interlocked.Read(ref Attempts),
                Interlocked.Read(ref Retries),
                Interlocked.Read(ref Succeeded),
                Interlocked.Read(ref Failed),
                Interlocked.Read(ref TimedOut),
                Interlocked.Read(ref Rejected),
                Interlocked.Read(ref Cancelled),
                Volatile.Read(ref InFlight),
                Volatile.Read(ref Queued),
                Volatile.Read(ref CircuitOpen) == 1,
                executions == 0 ? 0 : TimeSpan.FromTicks(ticks).TotalMilliseconds / executions);
        }
    }
}

public sealed class ExternalDependencyPolicy : IExternalDependencyPolicy, IDisposable
{
    private static readonly ActivitySource ActivitySource = new("Zumbo.ExternalDependencies", "1.0.0");
    private readonly string dependency;
    private readonly ExternalDependencyPolicyOptions options;
    private readonly ExternalDependencyTelemetry telemetry;
    private readonly IExternalDependencyJitter jitter;
    private readonly ILogger logger;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim bulkhead;
    private readonly object circuitGate = new();
    private int waiting;
    private int consecutiveFailures;
    private DateTimeOffset circuitOpenUntil;
    private bool halfOpenProbeInProgress;

    public ExternalDependencyPolicy(
        string dependency,
        ExternalDependencyPolicyOptions options,
        ExternalDependencyTelemetry telemetry,
        IExternalDependencyJitter jitter,
        ILogger logger,
        TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(dependency)) throw new ArgumentException("Dependency name is required.", nameof(dependency));
        options.Validate(dependency);
        this.dependency = dependency;
        this.options = options;
        this.telemetry = telemetry;
        this.jitter = jitter;
        this.logger = logger;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        bulkhead = new SemaphoreSlim(options.BulkheadLimit, options.BulkheadLimit);
    }

    public async Task<T> ExecuteAsync<T>(
        string operation,
        ExternalDependencyOperationKind operationKind,
        Func<CancellationToken, Task<T>> action,
        Func<Exception, bool>? isTransient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(action);
        using var activity = ActivitySource.StartActivity($"{dependency}.{operation}", ActivityKind.Client);
        activity?.SetTag("dependency.name", dependency);
        activity?.SetTag("dependency.operation", operation);
        activity?.SetTag("zumbo.operation_kind", operationKind.ToString());
        var halfOpenProbe = EnterCircuit(operation);
        var acquired = await EnterBulkheadAsync(operation, halfOpenProbe, cancellationToken);
        var started = Stopwatch.GetTimestamp();
        telemetry.ExecutionStarted(dependency, operation);
        try
        {
            var maximumAttempts = operationKind == ExternalDependencyOperationKind.NonIdempotentWrite
                ? 1
                : options.MaxRetryAttempts + 1;
            Exception? last = null;
            for (var attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                telemetry.Attempted(dependency);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(options.TimeoutMilliseconds));
                try
                {
                    var result = await action(timeout.Token).WaitAsync(timeout.Token);
                    ResetCircuit();
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    telemetry.Completed(dependency, operation, started, "success", false);
                    return result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ReleaseHalfOpenProbe(halfOpenProbe);
                    activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
                    telemetry.Completed(dependency, operation, started, "cancelled", IsCircuitOpen());
                    throw;
                }
                catch (OperationCanceledException exception)
                {
                    last = new ExternalDependencyTimeoutException(dependency, operation, exception);
                }
                catch (Exception exception) when (isTransient?.Invoke(exception) == true
                    || exception is ExternalDependencyTransientException)
                {
                    last = exception;
                }
                catch
                {
                    ReleaseHalfOpenProbe(halfOpenProbe);
                    activity?.SetStatus(ActivityStatusCode.Error, "non-transient");
                    telemetry.Completed(dependency, operation, started, "failure", IsCircuitOpen());
                    throw;
                }

                if (attempt == maximumAttempts) break;
                telemetry.Retried(dependency, operation);
                var exponential = Math.Min(
                    options.MaximumDelayMilliseconds,
                    options.BaseDelayMilliseconds * Math.Pow(2, attempt - 1));
                var delay = jitter.Apply(TimeSpan.FromMilliseconds(exponential), options.RetryJitterRatio);
                logger.LogWarning(
                    "External dependency {Dependency} operation {Operation} will retry attempt {Attempt} after {DelayMilliseconds} ms.",
                    dependency,
                    operation,
                    attempt + 1,
                    Math.Ceiling(delay.TotalMilliseconds));
                try
                {
                    await Task.Delay(delay, timeProvider, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ReleaseHalfOpenProbe(halfOpenProbe);
                    activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
                    telemetry.Completed(dependency, operation, started, "cancelled", IsCircuitOpen());
                    throw;
                }
            }

            RegisterFailure(halfOpenProbe);
            var outcome = last is ExternalDependencyTimeoutException ? "timeout" : "failure";
            activity?.SetStatus(ActivityStatusCode.Error, outcome);
            telemetry.Completed(dependency, operation, started, outcome, IsCircuitOpen());
            throw last ?? new ExternalDependencyTransientException("External dependency operation failed.");
        }
        finally
        {
            if (acquired) bulkhead.Release();
        }
    }

    public async Task ExecuteAsync(
        string operation,
        ExternalDependencyOperationKind operationKind,
        Func<CancellationToken, Task> action,
        Func<Exception, bool>? isTransient = null,
        CancellationToken cancellationToken = default) =>
        _ = await ExecuteAsync(
            operation,
            operationKind,
            async token => { await action(token); return true; },
            isTransient,
            cancellationToken);

    private bool EnterCircuit(string operation)
    {
        lock (circuitGate)
        {
            var now = timeProvider.GetUtcNow();
            if (circuitOpenUntil > now)
            {
                telemetry.Rejected(dependency, operation, "circuit-open");
                throw new ExternalDependencyCircuitOpenException(dependency);
            }

            if (circuitOpenUntil != default)
            {
                if (halfOpenProbeInProgress)
                {
                    telemetry.Rejected(dependency, operation, "half-open-probe");
                    throw new ExternalDependencyCircuitOpenException(dependency);
                }
                halfOpenProbeInProgress = true;
                return true;
            }
            return false;
        }
    }

    private async Task<bool> EnterBulkheadAsync(
        string operation,
        bool halfOpenProbe,
        CancellationToken cancellationToken)
    {
        if (await bulkhead.WaitAsync(0, cancellationToken)) return true;
        if (options.QueueLimit == 0 || Interlocked.Increment(ref waiting) > options.QueueLimit)
        {
            if (options.QueueLimit > 0) Interlocked.Decrement(ref waiting);
            telemetry.Rejected(dependency, operation, "bulkhead-saturated");
            ReleaseHalfOpenProbe(halfOpenProbe);
            throw new ExternalDependencyBulkheadRejectedException(dependency);
        }

        telemetry.Queued(dependency, 1);
        try
        {
            await bulkhead.WaitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            ReleaseHalfOpenProbe(halfOpenProbe);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref waiting);
            telemetry.Queued(dependency, -1);
        }
    }

    private void RegisterFailure(bool halfOpenProbe)
    {
        lock (circuitGate)
        {
            if (halfOpenProbe) halfOpenProbeInProgress = false;
            consecutiveFailures++;
            if (halfOpenProbe || consecutiveFailures >= options.CircuitFailureThreshold)
            {
                circuitOpenUntil = timeProvider.GetUtcNow().AddMilliseconds(options.CircuitBreakMilliseconds);
                telemetry.SetCircuit(dependency, true);
                logger.LogWarning(
                    "External dependency {Dependency} circuit opened for {BreakMilliseconds} ms after {FailureCount} failures.",
                    dependency,
                    options.CircuitBreakMilliseconds,
                    consecutiveFailures);
            }
        }
    }

    private void ResetCircuit()
    {
        lock (circuitGate)
        {
            consecutiveFailures = 0;
            circuitOpenUntil = default;
            halfOpenProbeInProgress = false;
            telemetry.SetCircuit(dependency, false);
        }
    }

    private void ReleaseHalfOpenProbe(bool halfOpenProbe)
    {
        if (!halfOpenProbe) return;
        lock (circuitGate) halfOpenProbeInProgress = false;
    }

    private bool IsCircuitOpen()
    {
        lock (circuitGate) return circuitOpenUntil > timeProvider.GetUtcNow();
    }

    public void Dispose() => bulkhead.Dispose();
}

public sealed class ExternalDependencyPolicyProvider(
    IConfiguration configuration,
    ExternalDependencyTelemetry telemetry,
    IExternalDependencyJitter jitter,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider) : IExternalDependencyPolicyProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, ExternalDependencyPolicy> policies = new(StringComparer.Ordinal);

    public IExternalDependencyPolicy Get(string dependency)
    {
        if (!ExternalDependencyNames.All.Contains(dependency, StringComparer.Ordinal))
            throw new InvalidOperationException($"Unknown external dependency policy '{dependency}'.");
        return policies.GetOrAdd(dependency, Create);
    }

    public IReadOnlyList<ExternalDependencySnapshot> GetSnapshots() => telemetry.GetSnapshots();

    private ExternalDependencyPolicy Create(string dependency)
    {
        var options = configuration.GetSection($"ExternalDependencies:{dependency}")
            .Get<ExternalDependencyPolicyOptions>() ?? new ExternalDependencyPolicyOptions();
        return new ExternalDependencyPolicy(
            dependency,
            options,
            telemetry,
            jitter,
            loggerFactory.CreateLogger($"Zumbo.ExternalDependencies.{dependency}"),
            timeProvider);
    }

    public void Dispose()
    {
        foreach (var policy in policies.Values) policy.Dispose();
    }
}

public static class ExternalDependencyServiceCollectionExtensions
{
    public static IServiceCollection AddZumboExternalDependencyResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        foreach (var dependency in ExternalDependencyNames.All)
        {
            var options = configuration.GetSection($"ExternalDependencies:{dependency}")
                .Get<ExternalDependencyPolicyOptions>() ?? new ExternalDependencyPolicyOptions();
            options.Validate(dependency);
        }
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ExternalDependencyTelemetry>();
        services.AddSingleton<IExternalDependencyJitter, RandomExternalDependencyJitter>();
        services.AddSingleton<IExternalDependencyPolicyProvider, ExternalDependencyPolicyProvider>();
        return services;
    }
}
