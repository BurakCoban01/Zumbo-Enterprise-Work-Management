using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class AuditParseDiagnosticsTests
{
    [Fact]
    public async Task MalformedSensitiveValuesPreserveRedactedFallbackAndEmitSafeDiagnostics()
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = Listen(measurements);
        var repository = new InMemoryDocumentRepository<AuditLogDocument>();
        var service = CreateService(repository);
        const string oldSecret = """{"password":"must-not-leak" """;
        const string newSecret = """{"token":"must-not-leak" """;

        await service.WriteAsync(
            "Malformed",
            "WorkItem",
            "item-1",
            oldSecret,
            newSecret,
            "correlation-malformed",
            CancellationToken.None);

        var stored = await repository.SelectAsync(x => x.Action == "Malformed");
        Assert.NotNull(stored);
        Assert.Equal("[REDACTED]", stored.OldValue);
        Assert.Equal("[REDACTED]", stored.NewValue);
        Assert.All(stored.Changes, change => Assert.True(change.Redacted));
        Assert.Equal(2, measurements.Count);
        Assert.All(measurements, measurement =>
        {
            Assert.Equal(1, measurement.Value);
            Assert.Contains(measurement.Tags, tag =>
                tag.Key == "reason" && Equals(tag.Value, "malformed_json"));
            Assert.Contains(measurement.Tags, tag =>
                tag.Key == "sensitive" && Equals(tag.Value, true));
            Assert.DoesNotContain(measurement.Tags, tag =>
                tag.Value?.ToString()?.Contains("must-not-leak", StringComparison.Ordinal) == true);
        });
        Assert.Equal(
            ["new", "old"],
            measurements
                .SelectMany(measurement => measurement.Tags)
                .Where(tag => tag.Key == "side")
                .Select(tag => tag.Value?.ToString())
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OversizedValueEmitsOneBoundedDiagnosticWithoutChangingObjectDiff()
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = Listen(measurements);
        var repository = new InMemoryDocumentRepository<AuditLogDocument>();
        var service = CreateService(repository);
        var largeValue = new string('a', 40_000);

        await service.WriteAsync(
            "Oversized",
            "WorkItem",
            "item-1",
            """{"name":"before"}""",
            $$"""{"name":"{{largeValue}}"}""",
            "correlation-oversized",
            CancellationToken.None);

        var stored = await repository.SelectAsync(x => x.Action == "Oversized");
        Assert.NotNull(stored);
        var change = Assert.Single(stored.Changes);
        Assert.Equal("name", change.Field);
        Assert.Equal("before", change.OldValue);
        Assert.Equal(4_000, change.NewValue!.Length);
        var measurement = Assert.Single(measurements);
        Assert.Contains(measurement.Tags, tag =>
            tag.Key == "reason" && Equals(tag.Value, "oversized"));
        Assert.Contains(measurement.Tags, tag =>
            tag.Key == "size" && Equals(tag.Value, "large"));
        Assert.DoesNotContain(measurement.Tags, tag =>
            tag.Value?.ToString()?.Contains(largeValue[..100], StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task ValidObjectAndScalarFallbackDoNotEmitParseFailureDiagnostics()
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = Listen(measurements);
        var repository = new InMemoryDocumentRepository<AuditLogDocument>();
        var service = CreateService(repository);

        await service.WriteAsync(
            "ValidObject",
            "WorkItem",
            "item-1",
            """{"name":"before"}""",
            """{"name":"after"}""",
            "correlation-object",
            CancellationToken.None);
        await service.WriteAsync(
            "ValidScalar",
            "WorkItem",
            "item-1",
            "before",
            "after",
            "correlation-scalar",
            CancellationToken.None);

        var scalar = await repository.SelectAsync(x => x.Action == "ValidScalar");
        Assert.NotNull(scalar);
        Assert.Equal("before", scalar.OldValue);
        Assert.Equal("after", scalar.NewValue);
        Assert.Empty(measurements);
    }

    private static MeterListener Listen(
        List<(long Value, KeyValuePair<string, object?>[] Tags)> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Zumbo.Audit"
                && instrument.Name == "zumbo.audit.diff_parse_fallbacks")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Add((value, tags.ToArray())));
        listener.Start();
        return listener;
    }

    private static AuditService CreateService(
        InMemoryDocumentRepository<AuditLogDocument> repository) =>
        new(
            repository,
            new FixedClock(),
            new FixedCurrentUser(),
            new EmptyRequestContext(),
            new AllowAccessChecker(),
            Options.Create(new AuditOptions()));

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedCurrentUser : ICurrentUser
    {
        public string? UserId => "user-audit";
        public string? OrganizationId => "org-audit";
        public IReadOnlyCollection<string> Roles => ["OrganizationAdmin"];
    }

    private sealed class EmptyRequestContext : IAuditRequestContext
    {
        public AuditRequestMetadata GetMetadata() => new(null, null);
    }

    private sealed class AllowAccessChecker : IAuditAccessChecker
    {
        public Task<AuditReadScope> EnsureCanReadAsync(
            AuditLogQuery query,
            CancellationToken ct) =>
            Task.FromResult(new AuditReadScope("org-audit"));
    }
}
