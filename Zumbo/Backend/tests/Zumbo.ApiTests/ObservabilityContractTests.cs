using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Runtime;

namespace Zumbo.ApiTests;

public sealed class ObservabilityContractTests
{
    [Fact]
    public async Task ExternalPolicy_EmitsSanitizedClientSpanWithOperationContext()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Zumbo.ExternalDependencies",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);
        using var telemetry = new ExternalDependencyTelemetry();
        using var policy = new ExternalDependencyPolicy(
            ExternalDependencyNames.Webhook,
            new ExternalDependencyPolicyOptions
            {
                TimeoutMilliseconds = 1_000,
                MaxRetryAttempts = 0,
                CircuitFailureThreshold = 2,
                CircuitBreakMilliseconds = 1_000,
                BulkheadLimit = 1,
                QueueLimit = 0
            },
            telemetry,
            new FixedJitter(),
            NullLogger.Instance);

        await policy.ExecuteAsync(
            "send",
            ExternalDependencyOperationKind.NonIdempotentWrite,
            _ => Task.CompletedTask);

        var activity = Assert.Single(stopped);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("webhook", activity.GetTagItem("dependency.name"));
        Assert.Equal("send", activity.GetTagItem("dependency.operation"));
        Assert.Equal("NonIdempotentWrite", activity.GetTagItem("zumbo.operation_kind"));
        Assert.DoesNotContain(activity.TagObjects, tag =>
            tag.Key.Contains("payload", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RealtimeMeter_TracksConnectionDeltaWithoutIdentityLabels()
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Zumbo.Realtime") meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Add((value, tags.ToArray())));
        listener.Start();

        RealtimeTelemetry.ActiveConnections.Add(1);
        RealtimeTelemetry.ActiveConnections.Add(-1);

        Assert.Equal([1L, -1L], measurements.Select(item => item.Value));
        Assert.All(measurements, item => Assert.Empty(item.Tags));
    }

    [Fact]
    public void Options_RejectUnsafeOrUnboundedExporterConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() => new ObservabilityOptions
        {
            OtlpEndpoint = "file:///tmp/telemetry"
        }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ObservabilityOptions
        {
            TraceSampleRatio = 1.1
        }.Validate());
        Assert.Throws<InvalidOperationException>(() => new ObservabilityOptions
        {
            MaxExportBatchSize = 1_000,
            MaxExportQueueSize = 100
        }.Validate());
    }

    [Fact]
    public void OptionalStack_DashboardAlertsAndRunbookRemainExplicitAndPrivate()
    {
        var backend = BackendRoot();
        var compose = File.ReadAllText(Path.Combine(backend, "docker-compose.observability.yml"));
        var alerts = File.ReadAllText(Path.Combine(backend, "observability", "alerts.yml"));
        var dashboard = File.ReadAllText(Path.Combine(backend, "observability", "grafana", "dashboards", "zumbo-overview.json"));
        var runbook = File.ReadAllText(Path.Combine(backend, "..", "docs", "operations", "observability-slo-runbook.md"));

        Assert.Contains("profiles: [\"observability\"]", compose);
        Assert.Contains("127.0.0.1", compose);
        Assert.Contains("Observability__OtlpEnabled: \"true\"", compose);
        Assert.Contains("ZumboApiHighErrorBudgetBurn", alerts);
        Assert.Contains("ZumboOutboxOldestPendingHigh", alerts);
        Assert.Contains("HTTP p95 latency", dashboard);
        Assert.Contains("Exporter failure", runbook);
        Assert.Contains("Metric labels never include tenant, user or project identifiers", runbook);
    }

    private static string BackendRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class FixedJitter : IExternalDependencyJitter
    {
        public TimeSpan Apply(TimeSpan delay, double ratio) => delay;
    }
}
