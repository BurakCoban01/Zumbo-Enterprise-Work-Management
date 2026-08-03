using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Runtime;

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
